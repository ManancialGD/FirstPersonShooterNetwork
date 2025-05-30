using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Assertions;
using Unity.Cinemachine;
using System.Linq;

[RequireComponent(typeof(FirstPersonCamera), typeof(FirstPersonMovement), typeof(FirstPersonSkin))]
public class FirstPersonController : NetworkBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference shootAction;

    private FirstPersonCamera cameraController;
    private FirstPersonMovement movementController;
    private FirstPersonSkin skinController;
    private FirstPersonShooter shootingController;
    private Rigidbody rb;

    private uint currentTick = 0;

    private const int BUFFER_SIZE = 4096;

    private readonly InputEntry[] inputBuffer = new InputEntry[BUFFER_SIZE];
    private int bufferHead = 0;  // next write position
    private int bufferTail = 0;  // oldest unacknowledged input
    private int bufferCount = 0;

    private void Awake()
    {
        cameraController = GetComponentInChildren<FirstPersonCamera>();
        movementController = GetComponent<FirstPersonMovement>();
        skinController = GetComponent<FirstPersonSkin>();
        shootingController = GetComponent<FirstPersonShooter>();
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (NetworkManager.Singleton.IsClient)
            Debug.Log($"[Client {NetworkManager.Singleton.LocalClientId}] IsOwner = {IsOwner}, OwnerClientId = {NetworkObject.OwnerClientId}");

        if (IsClient && !IsOwner)
        {
            GetComponentInChildren<CinemachineCamera>()?.gameObject.SetActive(false);
            rb.isKinematic = true;
        }

        if (!IsServer)
            Cursor.lockState = CursorLockMode.Locked;

        if (IsOwner)
        {
            skinController.InitiateLocal();
        }
        else
        {
            skinController.InitiateRemote();
        }
    }

    private void Update()
    {
        skinController.UpdateSkin();
        skinController.UpdateAnimation();

        if (IsOwner)
        {
            if (shootAction.action.IsPressed())
            {
                shootingController.Shoot();
            }
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            ProcessMovementInput();
            currentTick++;
        }
    }

    private void ProcessMovementInput()
    {
        moveAction.action.TryReadValue(out Vector2 moveInput);

        bool didJump = jumpAction.action.triggered;

        float deltaTime = Time.fixedDeltaTime;

        if (didJump)
            movementController.Jump();

        movementController.UpdateMovement(moveInput.normalized, deltaTime);

        AddToBuffer(new InputEntry
        {
            tick = currentTick,
            moveInput = moveInput.normalized,
            isJump = didJump,
            predictedPosition = transform.position,
            velocity = rb.linearVelocity,
        });

        if (didJump)
            JumpServerRpc(currentTick);
        else
            MoveServerRpc(moveInput.normalized, deltaTime, currentTick);
    }

    private void AddToBuffer(InputEntry entry)
    {
        if (bufferCount == BUFFER_SIZE)
        {
            bufferTail = (bufferTail + 1) % BUFFER_SIZE;
            bufferCount--;
        }

        inputBuffer[bufferHead] = entry;
        bufferHead = (bufferHead + 1) % BUFFER_SIZE;
        bufferCount++;
    }

    [ServerRpc]
    private void MoveServerRpc(Vector2 input, float deltaTime, uint tick)
    {
        movementController.UpdateMovement(input.normalized, deltaTime);
        MoveClientRpc(transform.position, tick);
    }

    [ClientRpc]
    private void MoveClientRpc(Vector3 serverPosition, uint serverTick)
    {
        if (!IsOwner) return;
        ReconcileState(serverPosition, serverTick);
    }

    [ServerRpc]
    private void JumpServerRpc(uint tick)
    {
        movementController.Jump();
        JumpClientRpc(transform.position, tick);
    }

    [ClientRpc]
    private void JumpClientRpc(Vector3 serverPosition, uint serverTick)
    {
        if (!IsOwner) return;
        ReconcileState(serverPosition, serverTick);
    }

    private void ReconcileState(Vector3 serverPosition, uint serverTick)
    {
        InputEntry inputEntry = default;
        bool found = false;
        int currentIndex = bufferTail;

        // Iterate through valid entries to find the serverTick
        for (int i = 0; i < bufferCount; i++)
        {
            if (inputBuffer[currentIndex].tick == serverTick)
            {
                inputEntry = inputBuffer[currentIndex];
                found = true;
                break;
            }
            currentIndex = (currentIndex + 1) % BUFFER_SIZE;
        }

        if (!found)
        {
            Debug.LogWarning($"Reconcile failed: No input entry found for tick {serverTick}");
            return;
        }

        if (Vector3.Distance(inputEntry.predictedPosition, serverPosition) > 1f)
        {
            Debug.Log("We need to reconcile state!");

            // Remove acknowledged inputs
            while (bufferCount > 0 && inputBuffer[bufferTail].tick <= serverTick)
            {
                bufferTail = (bufferTail + 1) % BUFFER_SIZE;
                bufferCount--;
            }

            // Restore state from the server's position
            transform.position = serverPosition;
            rb.linearVelocity = inputEntry.velocity;

            // Replay unacknowledged inputs
            int current = bufferTail;
            for (int i = 0; i < bufferCount; i++)
            {
                ref InputEntry entry = ref inputBuffer[current];
                rb.linearVelocity = entry.velocity;

                if (entry.isJump)
                    movementController.Jump();
                else
                    movementController.UpdateMovement(entry.moveInput, Time.fixedDeltaTime);

                // Update predicted state after applying each input
                entry.predictedPosition = transform.position;
                entry.velocity = rb.linearVelocity;

                current = (current + 1) % BUFFER_SIZE;
            }
        }
    }

    private void OnLookActionPerformed(InputAction.CallbackContext context)
    {
        Vector2 v = context.ReadValue<Vector2>();
        cameraController.UpdateView(v);
    }

    private void OnJumpActionPerformed(InputAction.CallbackContext context)
    {
        if (IsOwner && context.performed)
        {
            movementController.Jump();

            AddToBuffer(new InputEntry
            {
                tick = currentTick,
                isJump = true,
                predictedPosition = transform.position
            });

            JumpServerRpc(currentTick);
        }
    }

    private void OnEnable()
    {
        lookAction.action.performed += OnLookActionPerformed;
        jumpAction.action.performed += OnJumpActionPerformed;
    }

    private void OnDisable()
    {
        lookAction.action.performed -= OnLookActionPerformed;
        jumpAction.action.performed -= OnJumpActionPerformed;
    }
}
