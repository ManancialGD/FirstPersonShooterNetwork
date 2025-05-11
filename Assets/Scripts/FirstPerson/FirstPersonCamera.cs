using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class FirstPersonCamera : MonoBehaviour
{
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
    [SerializeField] private float maxAngleUp = 15;
    [Tooltip("Maximum downward angle the camera can rotate.")]
    [SerializeField] private float maxAngleDown = 45;

    [Header("Inversion Settings")]
    [Tooltip("Invert the Y-axis for mouse input.")]
    [SerializeField] private bool invertMouseY = false;

    public void UpdateView()
    {
        Vector2 rotation = Vector2.zero;

        try
        {
            rotation = lookAction.action.ReadValue<Vector2>();
        }
        catch (Exception e)
        {
            Debug.LogError("Error reading look input: " + e.Message);
        }

        rotation.x *= senseX * .15f;
        rotation.y *= senseY * .15f;

        rotation.y *= invertMouseY ? 1 : -1;

        cameraTarget.transform.rotation *= Quaternion.AngleAxis(rotation.x * senseX, Vector3.up);
        cameraTarget.transform.rotation *= Quaternion.AngleAxis(rotation.y * senseY, Vector3.right);

        Vector3 angles = cameraTarget.transform.localRotation.eulerAngles;
        angles.z = 0;

        float angle = cameraTarget.transform.localRotation.eulerAngles.x;

        if (angle > 180 && angle < 360 - maxAngleUp)
        {
            angles.x = 360 - maxAngleUp;
        }
        else if (angle < 180 && angle > maxAngleDown)
        {
            angles.x = maxAngleDown;
        }

        cameraTarget.transform.localRotation = Quaternion.Euler(angles);
    }
}