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
using UnityChess;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using IEnumerator = System.Collections.IEnumerator;

public class NetworkManagerHandler : MonoBehaviourSingleton<NetworkManagerHandler>
{
    [SerializeField] private int maxConnections = 2;
    private GameObject loginPanel, menuPanel;

    bool isHosting = true;
    private bool started, isGameActive, isAuthenticated;

    private Transform joinCodeObj;
    private TextMeshProUGUI title, pingText, loginStatusText, joinCodeStatusText;
    TMP_InputField usernameInput, passwordInput;

    MainThreadDispatcher mainThreadDispatcher;

    private void Awake()
    {
        if (Instance != this)
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
    }

    void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client connected: {clientId}");
        if (isHosting && NetworkManager.Singleton.ConnectedClients.Count == 2)
        {
            if (isGameActive)
            {
                // GameManager.Instance.SyncNetworkVariablesServerRpc();
                
                GameManager.Instance.ResumeGame();

                GameManager.Instance.SendGameStateServerRpc(clientId);
                
                // Request the client to send their avatar
                RequestClientAvatarClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new List<ulong> { clientId }
                    }
                });
                return;
            }

            isGameActive = true;
            GameManager.Instance.StartGameServerRpc(clientId);
        }
    }

    [ClientRpc]
    void RequestClientAvatarClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (isHosting)
            return;

        // Client sends their avatar to the host
        StartCoroutine(SendClientAvatar());
    }

    IEnumerator SendClientAvatar()
    {
        if (FirebaseStorageHandler.Instance != null)
        {
            // Get the client's avatar texture
            Task<Texture2D> avatarTask = FirebaseStorageHandler.Instance.GetCurrentAvatarTexture();
            yield return new WaitUntil(() => avatarTask.IsCompleted);

            if (avatarTask.Exception == null && GameManager.Instance != null)
            {
                // Set the client's avatar locally and notify the host
                string avatarId = FirebaseStorageHandler.Instance.EquippedAvatarId;
                GameManager.Instance.SetAvatar(avatarTask.Result, false, avatarId);
            }
        }
    }

    void OnClientDisconnect(ulong clientId)
    {
        if (isHosting && GameManager.Instance != null)
        {
            GameManager.Instance.HandlePlayerDisconnectServerRpc(clientId);
            return;
        }

        // Client detects that the host disconnected - initiate host migration
        if (!isHosting && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            Debug.Log("Host has disconnected. Initiating host migration...");

            // Save the game state
            string migratedGameState = SaveGameState();
            bool isBlackPlayer = GameManager.Instance.IsBlackPlayer();

            started = false;
            isGameActive = false;

            // Shutdown the network connection
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.Shutdown();

            // Load the MigrateHost scene instead of the Lobby
            SceneManager.LoadSceneAsync("Migrating").completed += operation =>
            {
                HandleMigrationComplete(migratedGameState, isBlackPlayer);
            };
        }
    }

    void HandleMigrationComplete(string migratedGameState, bool isBlackPlayer)
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;

        isHosting = true;
        isGameActive = true;

        StartGameAsMigratedHost(migratedGameState, isBlackPlayer);
    }

    void StartGameAsMigratedHost(string migratedGameState, bool isBlackPlayer)
    {
        if (started)
            return;

        Debug.Log("Starting game as migrated host (black player)");

        StartHostWithRelay().ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Failed to start host with relay during migration: {task.Exception}");
                return;
            }

            string joinCode = task.Result;
            Debug.Log($"Migration host relay join code: {joinCode}");

            mainThreadDispatcher.Enqueue(() =>
            {
                // Use specialized OnMigrationHost method
                OnMigrationHost(joinCode, migratedGameState, isBlackPlayer);
            });
        });
    }

    void OnMigrationHost(string joinCode, string gameState, bool isBlackPlayer)
    {
        Debug.Log("Setting up migrated host");
        started = true;

        NetworkManager.Singleton.SceneManager.OnLoadComplete += (id, sceneName, mode) =>
        {
            if (sceneName == "UnityChessGame")
            {
                mainThreadDispatcher.Enqueue(() =>
                {
                    GameManager.Instance.SetGameCode(joinCode);

                    // Configure GameManager for host migration (host plays black)
                    if (GameManager.Instance != null)
                    {
                        IEnumerator WaitForStart()
                        {
                            // Wait for start functions for GameManager to be complete (~1 second scrappy fix)
                            yield return new WaitForSeconds(1);
                            GameManager.Instance.SetupAsMigratedHostServerRpc(gameState, isBlackPlayer);
                        }

                        StartCoroutine(WaitForStart());
                    }
                });
            }
        };

        NetworkManager.Singleton.SceneManager.LoadScene("UnityChessGame", LoadSceneMode.Single);
    }

    // Save the current game state before host migration
    string SaveGameState()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("Game Manager is null during host migration");
            return "";
        }

        string gameState = GameManager.Instance.GetSerializedGame();
        if (string.IsNullOrEmpty(gameState))
        {
            Debug.LogWarning("Game state is empty during host migration");
        }

        GameManager.Instance.EndGameForHostMigration();
        return gameState;
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

                mainThreadDispatcher.Enqueue(() => { OnJoinOrHost(joinCode); });
            });
            return;
        }

        Transform startButton = GameObject.FindWithTag("StartButton").transform;
        startButton.GetChild(0).GetComponent<TextMeshProUGUI>().text = "JOIN";
        startButton.GetComponent<Button>().onClick.RemoveAllListeners();
        startButton.GetComponent<Button>().onClick.AddListener(JoinGame);
        GameObject.FindWithTag("Dropdown").SetActive(false);
        joinCodeObj.gameObject.SetActive(true);
        joinCodeStatusText = joinCodeObj.transform.Find("JoinStatus").GetComponent<TextMeshProUGUI>();
        title.text = "Enter Join Code";
    }

    void JoinGame()
    {
        string joinCode = joinCodeObj.GetComponent<TMP_InputField>().text;
        if (string.IsNullOrEmpty(joinCode))
        {
            joinCodeStatusText.text = "Join code cannot be empty";
            return;
        }

        Debug.Log($"Joining game with code: {joinCode}");
        joinCodeStatusText.text = "Joining game...";
        JoinWithRelay(joinCode).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Failed to join with relay: {task.Exception}");
                mainThreadDispatcher.Enqueue(() =>
                {
                    joinCodeStatusText.text = $"Failed to join with relay: {task.Exception?.Message}";
                });
                return;
            }

            mainThreadDispatcher.Enqueue(() => { OnJoinOrHost(joinCode); });
        });
    }

    void OnJoinOrHost(string joinCode, string gameState = null)
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
                    if (gameState != null)
                        GameManager.Instance.LoadGame(gameState);
                });
            }
        };

        if (isHosting)
            NetworkManager.Singleton.SceneManager.LoadScene("UnityChessGame",
                LoadSceneMode.Single);
    }

    public void IsHostingGame(int choice)
    {
        isHosting = choice == 0;
    }

    async void Login()
    {
        if (string.IsNullOrEmpty(usernameInput.text))
        {
            if (loginStatusText != null)
                loginStatusText.text = "Username cannot be empty";
            return;
        }

        if (loginStatusText != null)
            loginStatusText.text = "Signing in...";

        try
        {
            // Sign in with username/password
            if (!string.IsNullOrEmpty(passwordInput.text))
            {
                // Use password-based authentication
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(
                    usernameInput.text,
                    passwordInput.text);
            }
            else
            {
                // For demo purposes, use anonymous auth but set the player name
                SignInOptions loginOptions = new SignInOptions
                {
                    CreateAccount = true
                };
                await AuthenticationService.Instance.SignInAnonymouslyAsync(loginOptions);

                // Set player name to match username input
                await AuthenticationService.Instance.UpdatePlayerNameAsync(usernameInput.text);
            }

            Debug.Log($"Signed in to Unity services as: {AuthenticationService.Instance.PlayerId}");

            FirebaseService.Instance.UserID = AuthenticationService.Instance.PlayerId;
            loginPanel.SetActive(false);
            menuPanel.SetActive(true);
        }
        catch (Exception e)
        {
            Debug.LogError($"Login failed: {e.Message}");
            if (loginStatusText != null)
                loginStatusText.text = "Login failed: " + e.Message;
        }
    }

    async void Register()
    {
        if (string.IsNullOrEmpty(usernameInput.text) || string.IsNullOrEmpty(passwordInput.text))
        {
            if (loginStatusText != null)
                loginStatusText.text = "Username and password are required";
            return;
        }

        if (loginStatusText != null)
            loginStatusText.text = "Creating account...";

        try
        {
            // Sign up with username/password
            await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(
                usernameInput.text,
                passwordInput.text);

            Debug.Log($"Account created and signed in as: {AuthenticationService.Instance.PlayerId}");

            FirebaseService.Instance.UserID = AuthenticationService.Instance.PlayerId;
            loginPanel.SetActive(false);
            menuPanel.SetActive(true);
        }
        catch (Exception e)
        {
            Debug.LogError($"Registration failed: {e.Message}");
            if (loginStatusText != null)
                loginStatusText.text = "Registration failed: " + e.Message;
        }
    }

    async void Start()
    {
        mainThreadDispatcher = MainThreadDispatcher.Instance;
        
        GameObject pingCanvas = GameObject.FindWithTag("PingUI");
        GameObject debugConsole = GameObject.FindWithTag("DebugConsole");
        loginPanel = GameObject.FindWithTag("LoginPanel");

        DontDestroyOnLoad(debugConsole);

        pingText = pingCanvas.transform.GetChild(0).GetComponent<TextMeshProUGUI>();

        await InitialiseUnityServices();

        Button loginButton = loginPanel.transform.Find("Login").GetComponent<Button>();
        Button registerButton = loginPanel.transform.Find("Register").GetComponent<Button>();

        loginButton.onClick.AddListener(Login);
        registerButton.onClick.AddListener(Register);

        usernameInput = loginPanel.transform.Find("Username").GetComponent<TMP_InputField>();
        passwordInput = loginPanel.transform.Find("Password").GetComponent<TMP_InputField>();
        loginStatusText = loginPanel.transform.Find("LoginStatus").GetComponent<TextMeshProUGUI>();
        loginPanel.transform.Find("Exit").GetComponent<Button>().onClick.AddListener(QuitGame);

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }

        ResetState();

        menuPanel.SetActive(false);

        StartCoroutine(LogPingToConsole());
    }

    async Task InitialiseUnityServices()
    {
        try
        {
            await UnityServices.InitializeAsync();

            // if (!AuthenticationService.Instance.IsSignedIn)
            // {
            //     await AuthenticationService.Instance.SignInAnonymouslyAsync();
            //     Debug.Log($"Signed in as: {AuthenticationService.Instance.PlayerId}");
            // }

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

    async Task JoinWithRelay(string joinCode)
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
        if (NetworkManager.Singleton.IsClient)
        {
            float ping =
                NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);

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
        FirebaseService.Instance.SaveGame(sessionCode, serialisedGame);
    }

    public async Task<string> LoadGame(string sessionCode)
    {
        if (!isHosting)
            return null;

        return await FirebaseService.Instance.LoadGame(sessionCode);
    }

    public void ResetState()
    {
        if (!string.IsNullOrEmpty(FirebaseService.Instance.UserID))
        {
            loginPanel = GameObject.FindWithTag("LoginPanel");
            loginPanel.SetActive(false);
        }

        isHosting = true;
        started = false;
        isGameActive = false;

        Button startButton = GameObject.FindWithTag("StartButton").GetComponent<Button>();
        Button quitButton = GameObject.FindWithTag("QuitButton").GetComponent<Button>();
        TMP_Dropdown dropDown = GameObject.FindWithTag("Dropdown").GetComponent<TMP_Dropdown>();
        joinCodeObj = dropDown.transform.parent.Find("JoinCode");
        title = dropDown.transform.parent.Find("Title").GetComponent<TextMeshProUGUI>();

        menuPanel = startButton.transform.parent.gameObject;

        startButton.onClick.AddListener(() => { StartGame(); });
        quitButton.onClick.AddListener(QuitGame);
        dropDown.onValueChanged.AddListener(IsHostingGame);
        joinCodeObj.gameObject.SetActive(false);
    }

    IEnumerator LogPingToConsole()
    {
        while (true)
        {
            yield return new WaitForSeconds(5);
            Debug.Log($"Ping: {pingText.text}");
        }
    }
}