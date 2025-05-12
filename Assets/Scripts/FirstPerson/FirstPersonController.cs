using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(FirstPersonCamera), typeof(FirstPersonMovement), typeof(FirstPersonSkin))]
public class FirstPersonController : NetworkBehaviour
{
    private FirstPersonCamera cameraController;
    private FirstPersonMovement movementController;
    private FirstPersonSkin skinController;

    private void Awake()
    {
        cameraController = GetComponentInChildren<FirstPersonCamera>();
        movementController = GetComponent<FirstPersonMovement>();
        skinController = GetComponent<FirstPersonSkin>();
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
    }

    private void Update()
    {
        if (!IsClient || !IsOwner) return;

        cameraController.UpdateView();
        skinController.UpdateSkin();
        skinController.UpdateAnimation();
        movementController.UpdateMovement();
    }
}
