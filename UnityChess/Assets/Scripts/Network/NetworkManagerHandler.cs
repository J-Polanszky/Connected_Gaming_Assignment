using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

public class NetworkManagerHandler : MonoBehaviourSingleton<NetworkManagerHandler>
{
    [SerializeField] private int maxConnections = 2;

    bool isHosting = true;
    private bool started, isGameActive;

    private Transform joinCodeObj;

    private TextMeshProUGUI title, pingText;

    MainThreadDispatcher mainThreadDispatcher;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void SaveGameState()
    {
        // Save the game state
    }

    // async void MigrateHost()
    // {
    //     try
    //     {
    //         NetworkManager.Singleton.Shutdown();
    //     
    //         Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
    //     
    //         string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
    //         Debug.Log($"Join code: {joinCode}");
    //     
    //         UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
    //     
    //         transport.SetRelayServerData(
    //             allocation.RelayServer.IpV4,
    //             (ushort)allocation.RelayServer.Port,
    //             allocation.AllocationIdBytes,
    //             allocation.Key,
    //             allocation.ConnectionData
    //         );
    //     
    //         NetworkManager.Singleton.StartHost();
    //     
    //         // Display join code
    //         // TODO:   
    //     }
    //     catch (Exception e)
    //     {
    //         Debug.LogError($"Failed to migrate host: {e.Message}");
    //     }
    // }

    void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        if (isHosting && NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            if (isGameActive)
            {
                GameManager.Instance.ResumeGame();
                GameManager.Instance.SendGameStateServerRpc(clientId);
                return;
            }

            isGameActive = true;
            GameManager.Instance.StartGameServerRpc(clientId, true);
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        if (isHosting && GameManager.Instance != null)
        {
            GameManager.Instance.HandlePlayerDisconnectServerRpc(clientId);
            return;
        }

        // FIXME: This does not work as expected
        // // check if the client that disconnected was the host
        // if (clientId == NetworkManager.ServerClientId)
        // {
        //     // Save the game state
        //     SaveGameState();
        //     // Stop the unity relay server/lobby, and then create a new one with the non-disconnected client as the host
        //     // AKA Host Migration
        //     MigrateHost();
        // }
    }

    public void QuitGame()
    {
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

                Debug.Log("Sending join code to main thread");

                mainThreadDispatcher.Enqueue(() =>
                {
                    OnJoinOrHost(joinCode);
                });
            });
            return;
        }

        Transform startButton = GameObject.FindWithTag("StartButton").transform;
        startButton.GetChild(0).GetComponent<TextMeshProUGUI>().text = "JOIN";
        startButton.GetComponent<Button>().onClick.RemoveAllListeners();
        startButton.GetComponent<Button>().onClick.AddListener(JoinGame);
        GameObject.FindWithTag("Dropdown").SetActive(false);
        joinCodeObj.gameObject.SetActive(true);
        title.text = "Enter Join Code";
    }

    void JoinGame()
    {
        string joinCode = joinCodeObj.GetComponent<TMP_InputField>().text;
        Debug.Log($"Joining game with code: {joinCode}");
        JoinWithRelay(joinCode).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Failed to join with relay: {task.Exception}");
                return;
            }
            
            mainThreadDispatcher.Enqueue(() =>
            {
                OnJoinOrHost(joinCode);
            });
        });
    }

    void OnJoinOrHost(string joinCode)
    {
        Debug.Log("Executing on main thread");
        started = true;

        NetworkManager.Singleton.SceneManager.OnLoadComplete += (id, sceneName, mode) =>
        {
            if (sceneName == "UnityChessGame")
            {
                mainThreadDispatcher.Enqueue(() =>
                {
                    GameManager.Instance.SetGameCode(joinCode);
                });
                    
            }
        };
        
        if (isHosting)
            NetworkManager.Singleton.SceneManager.LoadScene("UnityChessGame",
                UnityEngine.SceneManagement.LoadSceneMode.Single);
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
        title = dropDown.transform.parent.Find("Title").GetComponent<TextMeshProUGUI>();

        GameObject pingCanvas = GameObject.FindWithTag("PingUI");
        GameObject debugConsole = GameObject.FindWithTag("DebugConsole");

        DontDestroyOnLoad(pingCanvas);
        DontDestroyOnLoad(debugConsole);

        pingText = pingCanvas.transform.GetChild(0).GetComponent<TextMeshProUGUI>();

        startButton.onClick.AddListener(StartGame);
        quitButton.onClick.AddListener(QuitGame);
        dropDown.onValueChanged.AddListener(IsHostingGame);
        joinCodeObj.gameObject.SetActive(false);

        await InitialiseUnityServices();

        mainThreadDispatcher = MainThreadDispatcher.Instance;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }
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
            
            UnityAnalyticsHandler.Instance.OnServicesInitialised();
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

    private void FixedUpdate()
    {
        if (!started)
            return;

        float ping = MeasurePing();

        if (ping < 0)
        {
            //TODO: Output some sort of text
        }

        SetPingText(ping);
    }

    void SetPingText(float ping)
    {
        int roundedPing = Mathf.RoundToInt(ping);
        pingText.text = $"{roundedPing}ms";
    }

    float MeasurePing()
    {
        //TODO: Test on another PC to see if this works, since with parrelsync it's always 0
        if (NetworkManager.Singleton.IsClient)
        {
            float ping =
                NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.Singleton
                    .LocalClientId);

            return ping;
        }

        return -1;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.DisconnectClient(NetworkManager.Singleton.LocalClientId);
            NetworkManager.Singleton.Shutdown();
        }
    }

    public void GameOver()
    {
        isGameActive = false;
    }
    
    public void RestartGame()
    {
        isGameActive = true;
    }

    public void SaveGame(string sessionCode, string serialisedGame)
    {
        if (!isHosting)
            return;

        FirebaseService.Instance.SaveGame(sessionCode, serialisedGame);
    }

    public async Task<string> LoadGame(string sessionCode)
    {
        if (!isHosting)
            return null;
        
        return await FirebaseService.Instance.LoadGame(sessionCode);
    }
}