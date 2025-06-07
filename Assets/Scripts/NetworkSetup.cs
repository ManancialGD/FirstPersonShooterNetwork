using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System;
using TMPro;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay.Models;
using Unity.Services.Relay;
using UnityEngine.UI;


#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

public class NetworkSetup : MonoBehaviour
{
    private bool isServer = false;

    private NetworkManager networkManager;
    private int playerPrefabIndex = 0;
    [SerializeField] private List<Transform> playerSpawnLocations;
    [SerializeField] private List<FirstPersonController> playerPrefabs;

    public event Action Connect;

    private UnityTransport transport;
    [SerializeField] private bool forceLocalServer;
    [SerializeField] private bool isRelay = true;
    [SerializeField] private int maxPlayers;
    [SerializeField] private TextMeshProUGUI textJoinCode;
    [SerializeField] private string joinCode;
    [SerializeField] private TMP_InputField inputJoinCodeField;
    [SerializeField] private Button enterButton;
    private RelayHostData relayData;

    private void Start()
    {
        // Parse command line arguments
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--server")
            {
                // --server found, this should be a server application
                isServer = true;
            }
            else if (args[i] == "--code")
            {
                joinCode = ((i + 1) < args.Length) ? (args[i + 1]) : ("");
            }
        }

        if (forceLocalServer) isServer = true;

        transport = GetComponent<UnityTransport>();

        if (transport.Protocol == UnityTransport.ProtocolType.RelayUnityTransport)
        {
            isRelay = true;
        }
        else
        {
            textJoinCode.gameObject.SetActive(false);
        }

        if (isServer)
            StartCoroutine(StartAsServerCR());
        else if (!string.IsNullOrEmpty(joinCode))
            StartCoroutine(StartAsClientCR());
        else
            enterButton.onClick.AddListener(OnEnterButtonClicked);

    }

    private void OnEnterButtonClicked()
    {
        if (string.IsNullOrEmpty(inputJoinCodeField.text))
        {
            Debug.LogError("Join code cannot be empty!");
            return;
        }

        joinCode = inputJoinCodeField.text.Trim();

        StartCoroutine(StartAsClientCR());
    }

    private IEnumerator StartAsServerCR()
    {
        SetWindowTitle("MPWyzard (server mode)");

        var networkManager = GetComponent<NetworkManager>();
        networkManager.enabled = true;
        transport.enabled = true;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Wait a frame for setups to be done
        yield return null;

        if (isRelay)
        {
            // Ensure maxPlayers is valid
            if (maxPlayers <= 0)
            {
                Debug.LogError("maxPlayers must be greater than 0 for Relay allocation!");
                yield break;
            }

            var loginTask = Login();
            yield return new WaitUntil(() => loginTask.IsCompleted);

            if (loginTask.Exception != null)
            {
                Debug.LogError("Login failed: " + loginTask.Exception);
                yield break;
            }

            Debug.Log("Login successful!");

            var allocationTask = CreateAllocationAsync(maxPlayers);
            yield return new WaitUntil(() => allocationTask.IsCompleted);

            if (allocationTask.Exception != null)
            {
                Debug.LogError("Allocation failed: " + allocationTask.Exception);
                yield break;
            }

            Debug.Log("Allocation successful!");

            Allocation allocation = allocationTask.Result;
            relayData = new RelayHostData();

            // Use the first endpoint
            foreach (var endpoint in allocation.ServerEndpoints)
            {
                relayData.IPv4Address = endpoint.Host;
                relayData.Port = (ushort)endpoint.Port;
                break;
            }

            relayData.AllocationID = allocation.AllocationId;
            relayData.AllocationIDBytes = allocation.AllocationIdBytes;
            relayData.ConnectionData = allocation.ConnectionData;
            relayData.Key = allocation.Key;

            // Set relay server data BEFORE starting the server
            transport.SetRelayServerData(
                relayData.IPv4Address,
                relayData.Port,
                relayData.AllocationIDBytes,
                relayData.Key,
                relayData.ConnectionData,
                isSecure: true
            );

            // Also set relay server data without isSecure for compatibility
            transport.SetRelayServerData(
                relayData.IPv4Address,
                relayData.Port,
                relayData.AllocationIDBytes,
                relayData.Key,
                relayData.ConnectionData
            );

            var joinCodeTask = GetJoinCodeAsync(relayData.AllocationID);
            yield return new WaitUntil(() => joinCodeTask.IsCompleted);

            if (joinCodeTask.Exception != null)
            {
                Debug.LogError("Join code failed: " + joinCodeTask.Exception);
                yield break;
            }

            Debug.Log("Code retrieved!");

            relayData.JoinCode = joinCodeTask.Result;

            if (textJoinCode != null)
            {
                textJoinCode.text = $"JoinCode:{relayData.JoinCode}";
                textJoinCode.gameObject.SetActive(true);
            }

            if (networkManager.StartServer())
            {
                Connect?.Invoke();
                enterButton.gameObject.SetActive(false);
                inputJoinCodeField.gameObject.SetActive(false);
                Debug.Log($"Relay server started on port {relayData.Port}...");
            }
            else
            {
                Debug.LogError($"Failed to start relay server on port {relayData.Port}...");
                SetWindowTitle("Failed to start relay server");
            }
        }
        else
        {
            if (networkManager.StartServer())
            {
                Debug.Log($"Serving on port {transport.ConnectionData.Port}...");
                Connect?.Invoke();
                enterButton.gameObject.SetActive(false);
                inputJoinCodeField.gameObject.SetActive(false);
            }
            else
            {
                Debug.LogError($"Failed to serve on port {transport.ConnectionData.Port}...");
                SetWindowTitle("Failed to start server");
            }
        }
    }
    private async Task<string> GetJoinCodeAsync(Guid allocationID)
    {
        try
        {
            string code = await RelayService.Instance.GetJoinCodeAsync(allocationID);
            return code;
        }
        catch (Exception e)
        {
            Debug.LogError("Error retrieving join code: " + e);
            throw;
        }
    }

    private async Task<bool> Login()
    {
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error login: " + e);
            throw;
        }

        return true;
    }
    private async Task<Allocation> CreateAllocationAsync(int maxPlayers)
    {
        try
        {
            // This requests space for maxPlayers + 1 connections (the +1 is for the server itself)
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
            return allocation;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error creating allocation: " + e);
            throw;
        }
    }

    IEnumerator StartAsClientCR()
    {
        SetWindowTitle("MPWyzard (client mode)");

        var networkManager = GetComponent<NetworkManager>();
        networkManager.enabled = true;
        transport.enabled = true;

        // Wait a frame for setups to be done
        yield return null;

        if (isRelay)
        {
            var loginTask = Login();
            yield return new WaitUntil(() => loginTask.IsCompleted);

            if (loginTask.Exception != null)
            {
                Debug.LogError("Login failed: " + loginTask.Exception);
                yield break;
            }

            Debug.Log("Login successful!");

            // Ask Unity Services for allocation data based on a join code
            var joinAllocationTask = JoinAllocationAsync(joinCode);
            yield return new WaitUntil(() => joinAllocationTask.IsCompleted);

            if (joinAllocationTask.Exception != null)
            {
                Debug.LogError("Join allocation failed: " + joinAllocationTask.Exception);
                yield break;
            }

            Debug.Log("Allocation joined!");
            relayData = new RelayHostData();

            var allocation = joinAllocationTask.Result;

            // Find the appropriate endpoint, just select the first one and use it
            foreach (var endpoint in allocation.ServerEndpoints)
            {
                relayData.IPv4Address = endpoint.Host;
                relayData.Port = (ushort)endpoint.Port;
                break;
            }

            relayData.AllocationID = allocation.AllocationId;
            relayData.AllocationIDBytes = allocation.AllocationIdBytes;
            relayData.ConnectionData = allocation.ConnectionData;
            relayData.HostConnectionData = allocation.HostConnectionData;
            relayData.Key = allocation.Key;

            transport.SetRelayServerData(
                relayData.IPv4Address,
                relayData.Port,
                relayData.AllocationIDBytes,
                relayData.Key,
                relayData.ConnectionData,
                relayData.HostConnectionData
            );
        }

        if (networkManager.StartClient())
        {
            Debug.Log($"Connecting on port {transport.ConnectionData.Port}...");
            Connect?.Invoke();
            enterButton.gameObject.SetActive(false);
            inputJoinCodeField.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogError($"Failed to connect on port {transport.ConnectionData.Port}...");
        }
    }

    private async Task<JoinAllocation> JoinAllocationAsync(string joinCode)
    {
        try
        {
            var allocation = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(joinCode);
            return allocation;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error joining allocation: " + e);
            throw;
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
        UnityEngine.Debug.Log($"Player {clientId} disconnected!");
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
