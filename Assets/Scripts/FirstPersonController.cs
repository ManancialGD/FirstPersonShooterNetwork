using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(FirstPersonCamera), typeof(FirstPersonMovement), typeof(FirstPersonSkin))]
public class FirstPersonController : MonoBehaviour
{
    private FirstPersonCamera cameraController;
    private FirstPersonMovement movementController;
    private FirstPersonSkin skinController;

    private void Awake()
    {
        cameraController = GetComponentInChildren<FirstPersonCamera>();
        movementController = GetComponent<FirstPersonMovement>();
        skinController = GetComponent<FirstPersonSkin>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        cameraController.UpdateView();
        movementController.UpdateInputs();
        skinController.UpdateSkin();
        skinController.UpdateAnimation();
    }

    private void FixedUpdate()
    {
        movementController.UpdateMovement();
    }
}
