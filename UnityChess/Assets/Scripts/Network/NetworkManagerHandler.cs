using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerHandler : MonoBehaviour
{
    [SerializeField] private int maxConnections = 2;

    bool isHosting = true;
    private bool started = false;

    private Transform joinCodeObj, title;

    public static NetworkManagerHandler Instance { get; private set; }

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

    private void OnEnable()
    {
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
    }

    private void OnDisable()
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    void SaveGameState()
    {
        // Save the game state
    }

    async void MigrateHost()
    {
        try
        {
            NetworkManager.Singleton.Shutdown();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Join code: {joinCode}");

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();

            // Display join code
            // TODO:   
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to migrate host: {e.Message}");
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        // check if the client that disconnected was the host
        if (clientId == NetworkManager.ServerClientId)
        {
            // Save the game state
            SaveGameState();
            // Stop the unity relay server/lobby, and then create a new one with the non-disconnected client as the host
            // AKA Host Migration
            MigrateHost();
        }
    }

    public void QuitGame()
    {
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
    }

    public void StartGame()
    {
        if (started)
            return;

        Debug.Log($"Am I hosting? {isHosting}");

        if (isHosting)
        {
            StartHostWithRelay().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError($"Failed to start host with relay: {task.Exception}");
                    return;
                }

                string joinCode = task.Result;
                Debug.Log($"Join code: {joinCode}");
                title.GetComponent<TextMeshProUGUI>().text = joinCode;
                started = true;
            });
            return;
        }

        Transform startButton = GameObject.FindWithTag("StartButton").transform;
        startButton.GetChild(0).GetComponent<TextMeshProUGUI>().text = "JOIN";
        startButton.GetComponent<Button>().onClick.RemoveAllListeners();
        startButton.GetComponent<Button>().onClick.AddListener(JoinGame);
        GameObject.FindWithTag("Dropdown").SetActive(false);
        joinCodeObj.gameObject.SetActive(true);
        title.gameObject.GetComponent<TextMeshProUGUI>().text = "Enter Join Code";
    }

    void JoinGame()
    {
        string code = joinCodeObj.GetComponent<TMP_InputField>().text;
        Debug.Log($"Joining game with code: {code}");
        JoinWithRelay(code).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Failed to join with relay: {task.Exception}");
                return;
            }

            Debug.Log("Joined game");
        });
    }

    public void IsHostingGame(int choice)
    {
        isHosting = choice == 0;
    }

    async void Start()
    {
        Button startButton = GameObject.FindWithTag("StartButton").GetComponent<Button>();
        Button quitButton = GameObject.FindWithTag("QuitButton").GetComponent<Button>();
        TMP_Dropdown dropDown = GameObject.FindWithTag("Dropdown").GetComponent<TMP_Dropdown>();
        joinCodeObj = dropDown.transform.parent.Find("JoinCode");
        title = dropDown.transform.parent.Find("Title");

        startButton.onClick.AddListener(StartGame);
        quitButton.onClick.AddListener(QuitGame);
        dropDown.onValueChanged.AddListener(IsHostingGame);
        joinCodeObj.gameObject.SetActive(false);

        await InitialiseUnityServices();
    }

    async Task InitialiseUnityServices()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"Signed in as: {AuthenticationService.Instance.PlayerId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($@"Failed to initialise Unity Services: {e.Message}");
        }
    }

    async Task<string> StartHostWithRelay()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Join code: {joinCode}");

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
                // TODO: Set secure?  
            );

            NetworkManager.Singleton.StartHost();

            return joinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start server with relay: {e.Message}");
            return string.Empty;
        }
    }

    async Task<bool> JoinWithRelay(string joinCode)
    {
        try
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                allocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to join with relay: {e.Message}");
            return false;
        }
    }
}