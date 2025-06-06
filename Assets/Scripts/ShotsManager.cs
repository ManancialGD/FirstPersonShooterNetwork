using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class ShotsManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int tickRate = 128;

    [SerializeField] private LayerMask worldLayerMask;
    [SerializeField] private LayerMask ragdollLayerMask;

    private int bufferSize;
    private float tickInterval;
    private float nextTickTime;

    public static ShotsManager Instance { get; private set; }

    private Dictionary<ulong, WorldData>[] buffer;
    private ulong[] cachedClientIds;
    private float lastCacheUpdateTime;

    private const float CLIENT_CACHE_UPDATE_INTERVAL = 1f;

    private int currentIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        bufferSize = tickRate;
        tickInterval = 1f / tickRate;

        buffer = new Dictionary<ulong, WorldData>[bufferSize];

        for (int i = 0; i < bufferSize; i++)
        {
            buffer[i] = new Dictionary<ulong, WorldData>();
        }

        currentIndex = 0;

        UpdateClientCache(force: true);
    }

    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        UpdateClientCache();

        if (Time.time >= nextTickTime)
        {
            RecordWorldState();
            nextTickTime = Time.time + tickInterval;
        }
    }

    public Vector3 CalculateShoot(float shotTime, ulong shooterId)
    {
        if (!NetworkManager.Singleton.IsServer) return default;

        if (!TryFindBestSnapshot(shotTime, shooterId, out var snapshot))
        {
            return default;
        }

        var originalStates = CaptureWorldState();
        RewindWorldToSnapshot(snapshot);
        Vector3 shotPos = ProcessShot(shooterId);
        RestoreWorldState(originalStates);

        return shotPos;
    }

    private void UpdateClientCache(bool force = false)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        float currentTime = Time.time;
        if (!force && currentTime - lastCacheUpdateTime < CLIENT_CACHE_UPDATE_INTERVAL)
            return;

        cachedClientIds = NetworkManager.Singleton.ConnectedClientsIds.ToArray();
        lastCacheUpdateTime = currentTime;
    }

    private void RecordWorldState()
    {
        if (cachedClientIds?.Length == 0 || cachedClientIds == null)
            return;

        buffer[currentIndex].Clear();

        foreach (ulong clientId in cachedClientIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                continue;
            if (client.PlayerObject == null)
                continue;

            if (!client.PlayerObject.TryGetComponent<FirstPersonCamera>(out var camera))
                continue;

            buffer[currentIndex][clientId] = new WorldData
            {
                Time = NetworkManager.Singleton.ServerTime.TimeAsFloat,
                Position = client.PlayerObject.transform.position,
                HeadRotation = camera.networkRotation.Value
            };
        }

        currentIndex = (currentIndex + 1) % bufferSize;
    }

    private bool TryFindBestSnapshot(float targetTime, ulong shooterId, out Dictionary<ulong, WorldData> snapshot)
    {
        snapshot = null;
        int newestIndex = (currentIndex - 1 + bufferSize) % bufferSize;

        int closestIndex = -1;
        float closestDiff = float.MaxValue;

        // Search backwards through buffer
        for (int i = 0; i < bufferSize; i++)
        {
            int bufferIndex = (newestIndex - i + bufferSize) % bufferSize;
            var currentSnapshot = buffer[bufferIndex];

            // Skip if shooter data doesn't exist in this snapshot
            if (!currentSnapshot.TryGetValue(shooterId, out var shooterData))
                continue;

            float timeDiff = Mathf.Abs(shooterData.Time - targetTime);

            // Found exact match (within 1 tick tolerance)
            if (timeDiff < tickInterval * 0.5f)
            {
                closestIndex = bufferIndex;
                break;
            }

            // Found closer match
            if (timeDiff < closestDiff)
            {
                closestDiff = timeDiff;
                closestIndex = bufferIndex;
            }
        }

        if (closestIndex == -1) return false;

        snapshot = buffer[closestIndex];
        return true;
    }

    private Dictionary<ulong, WorldData> CaptureWorldState()
    {
        var states = new Dictionary<ulong, WorldData>();

        foreach (ulong clientId in cachedClientIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                continue;

            if (!client.PlayerObject.TryGetComponent<FirstPersonCamera>(out var camera))
                continue;

            states[clientId] = new WorldData
            {
                Position = client.PlayerObject.transform.position,
                HeadRotation = camera.networkRotation.Value
            };
        }

        return states;
    }

    private void RewindWorldToSnapshot(Dictionary<ulong, WorldData> snapshot)
    {
        Physics.simulationMode = SimulationMode.Script;

        foreach (ulong clientId in cachedClientIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                continue;

            if (!snapshot.TryGetValue(clientId, out var data))
                continue;

            var playerObj = client.PlayerObject;
            playerObj.transform.position = data.Position;

            if (playerObj.TryGetComponent<FirstPersonCamera>(out var camera))
            {
                camera.CameraTarget.rotation = Quaternion.Euler(data.HeadRotation);
            }
        }

        // Simulate one physics step to update collisions
        Physics.Simulate(tickInterval);
    }

    private Vector3 ProcessShot(ulong shooterId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterId, out var shooter))
            return default;

        if (!shooter.PlayerObject.TryGetComponent<FirstPersonCamera>(out var camera))
            return default;

        Vector3 origin = camera.CameraTarget.transform.position;
        Vector3 direction = camera.CameraTarget.forward;

        LayerMask layerMask = worldLayerMask | ragdollLayerMask;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, 500, layerMask))
        {
            if (hit.collider.TryGetComponent(out RagDollLimb hitObject))
            {
                if (hitObject.GetComponentInParent<NetworkObject>()?.OwnerClientId != shooterId)
                {
                    hitObject.Damage(hit.point, direction);
                }
            }
        }

        return hit.point;
    }

    private void RestoreWorldState(Dictionary<ulong, WorldData> originalStates)
    {
        foreach (ulong clientId in cachedClientIds)
        {
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                continue;

            if (!originalStates.TryGetValue(clientId, out var data))
                continue;

            var playerObj = client.PlayerObject;
            playerObj.transform.position = data.Position;

            if (playerObj.TryGetComponent<FirstPersonCamera>(out var camera))
            {
                camera.CameraTarget.rotation = Quaternion.Euler(data.HeadRotation);
            }
        }

        Physics.simulationMode = SimulationMode.FixedUpdate;
    }
}
