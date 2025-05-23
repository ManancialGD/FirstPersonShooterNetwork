using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonCamera : NetworkBehaviour
{
    [Header("Camera Settings")]
    [Header("Camera Target")]
    [Tooltip("The target transform the camera will follow and rotate around.")]
    [SerializeField] private Transform cameraTarget;

    [Header("Input Settings")]
    [Tooltip("The input action reference for looking around.")]
    [SerializeField] private InputActionReference lookAction;

    [Header("Sensitivity Settings")]
    [Tooltip("Horizontal sensitivity of the camera.")]
    [SerializeField, Range(0.5f, 15f)] private float senseX = 1;
    [Tooltip("Vertical sensitivity of the camera.")]
    [SerializeField, Range(0.5f, 15f)] private float senseY = 1;

    [Header("Angle Limits")]
    [Tooltip("Maximum upward angle the camera can rotate.")]
    [SerializeField] private float maxAngleUp = 80;
    [Tooltip("Maximum downward angle the camera can rotate.")]
    [SerializeField] private float maxAngleDown = 80;

    [Header("Inversion Settings")]
    [Tooltip("Invert the Y-axis for mouse input.")]
    [SerializeField] private bool invertMouseY = false;

    private float yaw;
    private float pitch;

    private readonly NetworkVariable<Vector2> networkRotation = new(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
        );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // If not the owner, update rotation from network variable
        if (!IsOwner)
        {
            networkRotation.OnValueChanged += OnRotationChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unregister the event when despawning to prevent memory leaks.
        if (!IsOwner)
        {
            networkRotation.OnValueChanged -= OnRotationChanged;
        }
    }

    private void OnRotationChanged(Vector2 previousValue, Vector2 newValue)
    {
        if (!IsOwner)
        {
            cameraTarget.localRotation = Quaternion.Euler(newValue.x, newValue.y, 0f);
        }
    }

    public void UpdateView(Vector2 lookInput)
    {
        if (!IsOwner) return;
        if (cameraTarget == null) return;

        if (lookInput == Vector2.zero)
            return;

        float mouseX = lookInput.x * senseX * 0.15f;
        float mouseY = lookInput.y * senseY * 0.15f * (invertMouseY ? 1f : -1f);

        yaw += mouseX;
        pitch += mouseY;

        // Clamp vertical rotation
        pitch = Mathf.Clamp(pitch, -maxAngleDown, maxAngleUp);

        // Apply rotation: yaw on Y, pitch on X
        cameraTarget.localRotation = Quaternion.Euler(pitch, yaw, 0f);

        networkRotation.Value = new Vector2(pitch, yaw);
    }
}