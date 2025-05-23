using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Assertions;
using Unity.Cinemachine;

[RequireComponent(typeof(FirstPersonCamera), typeof(FirstPersonMovement), typeof(FirstPersonSkin))]
public class FirstPersonController : NetworkBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference jumpAction;

    private FirstPersonCamera cameraController;
    private FirstPersonMovement movementController;
    private FirstPersonSkin skinController;

    private uint currentTick = 0;

    private const int BUFFER_SIZE = 128;

    private readonly InputEntry[] inputBuffer = new InputEntry[BUFFER_SIZE];
    private int bufferHead = 0;  // next write position
    private int bufferTail = 0;  // oldest unacknowledged input
    private int bufferCount = 0;

    private void Awake()
    {
        cameraController = GetComponentInChildren<FirstPersonCamera>();
        movementController = GetComponent<FirstPersonMovement>();
        skinController = GetComponent<FirstPersonSkin>();

        Assert.IsNotNull(moveAction);
        Assert.IsNotNull(lookAction);
        Assert.IsNotNull(jumpAction);
    }

    private void Start()
    {
        if (NetworkManager.Singleton.IsClient)
            Debug.Log($"[Client {NetworkManager.Singleton.LocalClientId}] IsOwner = {IsOwner}, OwnerClientId = {NetworkObject.OwnerClientId}");

        if (IsClient && !IsOwner)
        {
            GetComponentInChildren<CinemachineCamera>()?.gameObject.SetActive(false);
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

        if (IsClient)
        {
            if (TryGetComponent<Rigidbody>(out var rb))
                rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    private void Update()
    {
        skinController.UpdateSkin();
        skinController.UpdateAnimation();
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

        movementController.UpdateMovement(moveInput, deltaTime);

        AddToBuffer(new InputEntry
        {
            tick = currentTick,
            moveInput = moveInput,
            isJump = didJump,
            predictedPosition = transform.position
        });

        if (didJump)
            JumpServerRpc(currentTick);
        else
            MoveServerRpc(moveInput, deltaTime, currentTick);
    }

    private void AddToBuffer(InputEntry entry)
    {
        if (bufferCount == BUFFER_SIZE)
        {
            bufferTail = (bufferTail + 1) % BUFFER_SIZE;
            bufferCount--;
            Debug.LogWarning("Input buffer overflow! Increase buffer size.");
        }

        inputBuffer[bufferHead] = entry;
        bufferHead = (bufferHead + 1) % BUFFER_SIZE;
        bufferCount++;
    }

    [ServerRpc]
    private void MoveServerRpc(Vector2 input, float deltaTime, uint tick)
    {
        movementController.UpdateMovement(input, deltaTime);
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
        // Remove all inputs up to and including the serverTick (acknowledged)
        while (bufferCount > 0 && inputBuffer[bufferTail].tick <= serverTick)
        {
            bufferTail = (bufferTail + 1) % BUFFER_SIZE;
            bufferCount--;
        }

        // If client position is off from server’s authoritative position, correct and replay
        if (Vector3.Distance(transform.position, serverPosition) > 0.1f)
        {
            Debug.Log("We need to reconcile");

            // Try to rollback to predicted position of last confirmed tick if available
            int rollbackIndex = FindInputIndexByTick(serverTick);
            if (rollbackIndex >= 0)
                transform.position = inputBuffer[rollbackIndex].predictedPosition;
            else
                transform.position = serverPosition;

            // Replay all buffered inputs after serverTick
            int current = bufferTail;
            for (int i = 0; i < bufferCount; i++)
            {
                ref InputEntry entry = ref inputBuffer[current];

                entry.predictedPosition = transform.position;

                if (entry.isJump)
                    movementController.Jump();
                else
                    movementController.UpdateMovement(entry.moveInput, Time.fixedDeltaTime);


                current = (current + 1) % BUFFER_SIZE;
            }
        }
    }

    // Helper: Find the index in buffer for a given tick
    private int FindInputIndexByTick(uint tick)
    {
        int count = bufferCount;
        int index = bufferTail;
        for (int i = 0; i < count; i++)
        {
            if (inputBuffer[index].tick == tick)
                return index;
            index = (index + 1) % BUFFER_SIZE;
        }
        return -1;
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
