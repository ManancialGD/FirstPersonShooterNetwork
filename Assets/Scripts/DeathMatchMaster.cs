using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Linq;
using System.Collections.Generic;

public class DeathMatchMaster : MonoBehaviour
{
    [SerializeField] private float respawnTime = 1f;
    [SerializeField] private Transform[] spawnPoints;

    private HashSet<ulong> registeredClients;

    public bool IsServer => NetworkManager.Singleton.IsServer;

    private bool ready = false;

    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        yield return null;

        if (!IsServer)
            yield break;

        registeredClients = new HashSet<ulong>();
        Debug.Log("[DeathMatchMaster] Server started. Ready to register clients.");
        ready = true;
    }

    private void Update()
    {
        if (!IsServer || !ready) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null && !registeredClients.Contains(client.ClientId))
            {
                Debug.Log($"[DeathMatchMaster] Registering client {client.ClientId}.");
                RegisterClientEvent(client.ClientId);
            }
        }
    }

    private void RegisterClientEvent(ulong clientId)
    {
        if (!IsServer) return;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[DeathMatchMaster] Client {clientId} not found in ConnectedClients.");
            return;
        }
        if (!client.PlayerObject)
        {
            Debug.LogWarning($"[DeathMatchMaster] Client {clientId} has no PlayerObject.");
            return;
        }

        if (client.PlayerObject.TryGetComponent<HealthModule>(out var healthModule))
        {
            healthModule.Died += () => OnClientDead(clientId);
            registeredClients.Add(clientId);
            Debug.Log($"[DeathMatchMaster] Registered HealthModule.Died event for client {clientId}.");
        }
        else
        {
            Debug.LogWarning($"[DeathMatchMaster] Client {clientId} does not have a HealthModule component.");
        }
    }

    private void OnClientDead(ulong clientId)
    {
        if (!IsServer) return;

        Debug.Log($"[DeathMatchMaster] Client {clientId} has died.");
        StartCoroutine(RespawnClientAfterTime(clientId, respawnTime));
    }

    private IEnumerator RespawnClientAfterTime(ulong clientId, float respawnTime)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            Debug.LogWarning($"[DeathMatchMaster] Respawn failed: Client {clientId} not found.");
            yield break;
        }
        if (!client.PlayerObject)
        {
            Debug.LogWarning($"[DeathMatchMaster] Respawn failed: Client {clientId} has no PlayerObject.");
            yield break;
        }
        if (!IsServer) yield break;

        Debug.Log($"[DeathMatchMaster] Waiting {respawnTime} seconds to respawn client {clientId}.");
        yield return new WaitForSeconds(respawnTime);

        Debug.Log($"[DeathMatchMaster] Respawning client {clientId}.");

        if (client.PlayerObject.TryGetComponent<HealthModule>(out var healthModule))
        {
            healthModule.currentHealth.Value = 100;
            healthModule.ResetIsDead();
            healthModule.DeactivateRagdollClientRpc();
            healthModule.DeactivateRagdoll();
            Debug.Log($"[DeathMatchMaster] Reset health and ragdoll for client {clientId}.");
        }

        // Find a free spawn point
        var spawnPos = Vector3.zero;
        var currentPlayers = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

        foreach (var playerSpawnLocation in spawnPoints)
        {
            var closestDist = float.MaxValue;

            foreach (var player in currentPlayers)
            {
                float d = Vector3.Distance(player.transform.position, playerSpawnLocation.position);
                closestDist = Mathf.Min(closestDist, d);
            }

            if (closestDist > 20)
            {
                spawnPos = playerSpawnLocation.position;
                Debug.Log($"[DeathMatchMaster] Found spawn point for client {clientId} at {spawnPos}.");
                break;
            }
        }

        if (client.PlayerObject.TryGetComponent<FirstPersonController>(out var controller))
        {
            controller.transform.position = spawnPos;
            controller.ResetInputBufferClientRpc();
            if (controller.TryGetComponent(out FirstPersonSkin skinController))
            {
                skinController.InitiateLocalClientRpc();
                Debug.Log($"[DeathMatchMaster] Initiated local skin for client {clientId}.");
            }
            else
            {
                Debug.LogWarning($"[DeathMatchMaster] Client {clientId} does not have a FirstPersonSkin component.");
            }

            Debug.Log($"[DeathMatchMaster] Moved client {clientId} to spawn point.");
        }
        else
        {
            Debug.LogWarning($"[DeathMatchMaster] Client {clientId} does not have a FirstPersonController component.");
        }
    }
}
