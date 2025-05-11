using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;

#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using System.Linq;
using System.IO;
#endif

public class NetworkSetup : MonoBehaviour
{
    [SerializeField]
    private bool isServer = false;

    private NetworkManager networkManager;
    private int playerPrefabIndex = 0;
    [SerializeField] private List<Transform> playerSpawnLocations;
    [SerializeField] private List<FirstPersonController> playerPrefabs;

    private void Start()
    {
        // Parse command line arguments
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--server")
            {
                // --server found, this should be a server application
                isServer = true;
            }
        }

        networkManager = FindAnyObjectByType<NetworkManager>();

        if (isServer)
            StartCoroutine(StartAsServerCR());
        else
            StartCoroutine(StartAsClientCR());
    }

    private IEnumerator StartAsServerCR()
    {
        var networkManager = GetComponent<NetworkManager>();
        networkManager.enabled = true;

        var transport = GetComponent<UnityTransport>();
        transport.enabled = true;

        // Wait a frame for setups to be done
        yield return null;

        if (networkManager.StartServer())
        {
            SetWindowTitle("Server");
            Debug.Log($"Serving on port {transport.ConnectionData.Port}...");

            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
        else
        {
            SetWindowTitle("Failed to serve");
            Debug.LogError($"Failed to serve on port {transport.ConnectionData.Port}...");
        }
    }

    private IEnumerator StartAsClientCR()
    {
        var networkManager = GetComponent<NetworkManager>();
        networkManager.enabled = true;

        var transport = GetComponent<UnityTransport>();
        transport.enabled = true;

        // Wait a frame for setups to be done
        yield return null;

        if (networkManager.StartClient())
        {
            Debug.Log($"Connecting on port {transport.ConnectionData.Port}...");
            SetWindowTitle("Client");
        }
        else
        {
            Debug.LogError($"Failed to connect on port {transport.ConnectionData.Port}...");
            SetWindowTitle("Failed to connect");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        Debug.Log($"Player {clientId} connected, prefab index = {playerPrefabIndex}!");

        // Check a free spot for this player
        var spawnPos = Vector3.zero;
        var currentPlayers = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

        foreach (var playerSpawnLocation in playerSpawnLocations)
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
                break;
            }
        }

        // Spawn player object
        var spawnedObject = Instantiate(playerPrefabs[playerPrefabIndex], spawnPos, Quaternion.identity);
        var prefabNetworkObject = spawnedObject.GetComponent<NetworkObject>();
        prefabNetworkObject.SpawnAsPlayerObject(clientId, true);
        prefabNetworkObject.ChangeOwnership(clientId);
        playerPrefabIndex = (playerPrefabIndex + 1) % playerPrefabs.Count;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Player {clientId} disconnected!");
    }

#if UNITY_STANDALONE_WIN

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    static extern IntPtr EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    // Delegate to filter windows
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static IntPtr FindWindowByProcessId(uint processId)
    {
        IntPtr windowHandle = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            uint windowProcessId;
            GetWindowThreadProcessId(hWnd, out windowProcessId);
            if (windowProcessId == processId)
            {
                windowHandle = hWnd;
                return false; // Found the window, stop enumerating
            }
            return true; // Continue enumerating
        }, IntPtr.Zero);

        return windowHandle;
    }

    static void SetWindowTitle(string title)
    {
#if !UNITY_EDITOR
            uint processId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr hWnd = FindWindowByProcessId(processId);

            if (hWnd != IntPtr.Zero)
            {
                SetWindowText(hWnd, title);
            }
#endif
    }
#else
    static void SetWindowTitle(string title) { }
#endif
}
