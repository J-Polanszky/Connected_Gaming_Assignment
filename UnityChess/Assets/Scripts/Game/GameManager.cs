using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityChess;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages the overall game state, including game start, moves execution,
/// special moves handling (such as castling, en passant, and promotion), and game reset.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    private NetworkVariable<bool> isGameActive = new();
    private NetworkVariable<int> currentPlayerTurn = new(); // 0 = White, 1 = Black
    private NetworkVariable<ulong> whitePlayerId = new(ulong.MaxValue); // ID of the client playing white
    private NetworkVariable<ulong> blackPlayerId = new(ulong.MaxValue); // ID of the client playing black
    private NetworkVariable<FixedString128Bytes> whiteAvatar = new();
    private NetworkVariable<FixedString128Bytes> blackAvatar = new();

    private Transform gameInfo;
    private string gameCodeStr, serialisedGame;
    private TextMeshProUGUI gameCode;

    private Button leftButton, rightButton;
    private Text leftButtonText, rightButtonText;
    private TMP_InputField inputField;
    private Image white_avatar, black_avatar;

    // Events signalling various game state changes.
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    public static event Action MoveExecutedEvent;

    // Helper properties to check which side the local player is playing
    public bool IsPlayingWhite => NetworkManager.Singleton.LocalClientId == whitePlayerId.Value;
    public bool IsPlayingBlack => NetworkManager.Singleton.LocalClientId == blackPlayerId.Value;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// Gets the current board state from the game.
    /// </summary>
    public Board CurrentBoard
    {
        get
        {
            // Attempts to retrieve the current board from the board timeline.
            game.BoardTimeline.TryGetCurrent(out Board currentBoard);
            return currentBoard;
        }
    }

    /// <summary>
    /// Gets the side (White/Black) whose turn it is to move.
    /// </summary>
    public Side SideToMove
    {
        get
        {
            // Retrieves the current game conditions and returns the active side.
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            return currentConditions.SideToMove;
        }
    }

    /// <summary>
    /// Gets the side that started the game.
    /// </summary>
    public Side StartingSide => game.ConditionsTimeline[0].SideToMove;

    /// <summary>
    /// Gets the timeline of half-moves made in the game.
    /// </summary>
    public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;

    /// <summary>
    /// Gets the index of the most recent half-move.
    /// </summary>
    public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;

    /// <summary>
    /// Computes the full move number based on the starting side and the latest half-move index.
    /// </summary>
    public int FullMoveNumber => StartingSide switch
    {
        Side.White => LatestHalfMoveIndex / 2 + 1,
        Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
        _ => -1
    };

    private bool isWhiteAI;
    private bool isBlackAI;

    /// <summary>
    /// Gets a list of all current pieces on the board, along with their positions.
    /// </summary>
    public List<(Square, Piece)> CurrentPieces
    {
        get
        {
            // Clear the backing list before populating with current pieces.
            currentPiecesBacking.Clear();
            // Iterate over every square on the board.
            for (int file = 1; file <= 8; file++)
            {
                for (int rank = 1; rank <= 8; rank++)
                {
                    Piece piece = CurrentBoard[file, rank];
                    // If a piece exists at this position, add it to the list.
                    if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
                }
            }

            return currentPiecesBacking;
        }
    }

    // Backing list for storing current pieces on the board.
    private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();

    // Reference to the debug utility for the chess engine.
    [SerializeField] private UnityChessDebug unityChessDebug;

    // The current game instance.
    private Game game;

    // Serializers for game state (FEN and PGN formats).
    private FENSerializer fenSerializer;

    private PGNSerializer pgnSerializer;

    // Cancellation token source for asynchronous promotion UI tasks.
    private CancellationTokenSource promotionUITaskCancellationTokenSource;

    // Stores the user's choice for promotion; initialised to none.
    private ElectedPiece userPromotionChoice = ElectedPiece.None;

    // Mapping of game serialization types to their corresponding serializers.
    private Dictionary<GameSerializationType, IGameSerializer> serializersByType;

    // Currently selected serialization type (default is FEN).
    private GameSerializationType selectedSerializationType = GameSerializationType.FEN;

    /// <summary>
    /// Unity's Start method initialises the game and sets up event handlers.
    /// </summary>
    public void Start()
    {
        gameInfo = GameObject.FindWithTag("GameInfo").transform;
        gameCode = GameObject.FindWithTag("Gamecode").GetComponent<TextMeshProUGUI>();

        white_avatar = gameInfo.parent.Find("Avatar_White").GetComponent<Image>();
        black_avatar = gameInfo.parent.Find("Avatar_Black").GetComponent<Image>();

        GameObject leftButtonObj = GameObject.FindWithTag("LeftButton");
        GameObject rightButtonObj = GameObject.FindWithTag("RightButton");
        leftButton = leftButtonObj.GetComponent<Button>();
        rightButton = rightButtonObj.GetComponent<Button>();
        leftButtonText = leftButton.transform.GetChild(0).GetComponent<Text>();
        rightButtonText = rightButton.transform.GetChild(0).GetComponent<Text>();
        inputField = GameObject.FindWithTag("InputField").GetComponent<TMP_InputField>();

        leftButtonText.text = "Quit";
        leftButton.onClick.RemoveAllListeners();
        leftButton.onClick.AddListener(Quit);

        isGameActive.OnValueChanged += OnGameActiveChanged;
        currentPlayerTurn.OnValueChanged += OnPlayerTurnChanged;
        whitePlayerId.OnValueChanged += OnPlayerIdsChanged;
        blackPlayerId.OnValueChanged += OnPlayerIdsChanged;
        whiteAvatar.OnValueChanged += OnWhiteAvatarChanged;
        blackAvatar.OnValueChanged += OnBlackAvatarChanged;

        // Subscribe to the event triggered when a visual piece is moved.
        VisualPiece.VisualPieceMoved += OnNetworkedPieceMoved;

        // Initialise the serializers for FEN and PGN formats.
        serializersByType = new Dictionary<GameSerializationType, IGameSerializer>
        {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };

        SetClientIDServerRpc(NetworkManager.Singleton.LocalClientId);

        if (IsHost)
        {
            SetupInitialGameState();

            IEnumerator DelayedStart()
            {
                yield return new WaitForFixedUpdate();

                rightButtonText.text = "Load Game";
                rightButton.onClick.RemoveAllListeners();
                rightButton.onClick.AddListener(() =>
                {
                    NetworkManagerHandler.Instance.LoadGame(inputField.text).ContinueWith(
                        task =>
                        {
                            MainThreadDispatcher.Instance.Enqueue(() =>
                            {
                                LoadGame(task.Result);
                                BoardManager.Instance.SetActiveAllPieces(false);
                            });
                        });
                });
            }

            StartCoroutine(DelayedStart());
        }
        else
        {
            bool isHostWhite = whitePlayerId.Value != ulong.MaxValue ||
                               whitePlayerId.Value != NetworkManager.Singleton.LocalClientId;
            Task loadTask = LoadRemoteAvatar(whiteAvatar.Value.ToString(), isHostWhite);

            IEnumerator DelayedStart()
            {
                yield return new WaitForFixedUpdate();

                rightButtonText.text = "Save Game";
                rightButton.onClick.RemoveAllListeners();
                rightButton.onClick.AddListener(SaveGame);
            }

            StartCoroutine(DelayedStart());
        }


#if DEBUG_VIEW
        // Enable debug view if compiled with DEBUG_VIEW flag.
        unityChessDebug.gameObject.SetActive(true);
        unityChessDebug.enabled = true;
#endif
    }

    /// <summary>
    /// Serialises the current game state using the selected serialization format.
    /// </summary>
    /// <returns>A string representing the serialised game state.</returns>
    public string SerializeGame()
    {
        return serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer)
            ? serializer?.Serialize(game)
            : null;
    }

    public bool IsBlackPlayer()
    {
        return blackPlayerId.Value == NetworkManager.Singleton.LocalClientId;
    }

    /// <summary>
    /// Loads a game from the given serialised game state string.
    /// </summary>
    /// <param name="serializedGame">The serialised game state string.</param>
    public void LoadGame(string serializedGame)
    {
        game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
        NewGameStartedEvent?.Invoke();
        serialisedGame = serializedGame;
    }

    void Quit()
    {
        NetworkManager.Singleton.Shutdown();

        var eventInfo = typeof(NetworkSceneManager).GetField("OnLoadComplete",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (eventInfo != null)
            eventInfo.SetValue(NetworkManager.Singleton.SceneManager, null);

        SceneManager.LoadSceneAsync("Lobby").completed += operation => { NetworkManagerHandler.Instance.ResetState(); };
    }

    void Resign()
    {
        ulong clientID = NetworkManager.Singleton.LocalClientId;
        HandleResignationServerRpc(clientID);
    }

    void SaveGame()
    {
        string sessionCode = inputField.text;
        NetworkManagerHandler.Instance.SaveGame(sessionCode, serialisedGame);
    }

    [ServerRpc(RequireOwnership = false)]
    void HandleResignationServerRpc(ulong clientId)
    {
        if (!IsHost)
            return;

        bool didWhiteWin = clientId == blackPlayerId.Value;

        UnityAnalyticsHandler.Instance.RecordVictory(didWhiteWin,
            VictoryType.Resignation, gameCodeStr);

        GameEndClientRpc(isResignation: true, didWhiteWin: didWhiteWin);
    }

    /// <summary>
    /// Resets the game to a specific half-move index.
    /// </summary>
    /// <param name="halfMoveIndex">The target half-move index to reset the game to.</param>
    public void ResetGameToHalfMoveIndex(int halfMoveIndex)
    {
        // If the reset operation fails, exit early.
        if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;

        // Disable promotion UI and cancel any pending promotion tasks.
        UIManager.Instance.SetActivePromotionUI(false);
        promotionUITaskCancellationTokenSource?.Cancel();
        // Notify subscribers that the game has been reset to a half-move.
        GameResetToHalfMoveEvent?.Invoke();
    }

    /// <summary>
    /// Handles special move behaviour asynchronously (castling, en passant, and promotion).
    /// </summary>
    /// <param name="specialMove">The special move to process.</param>
    /// <returns>A task that resolves to true if the special move was handled; otherwise, false.</returns>
    private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove)
    {
        switch (specialMove)
        {
            // Handle castling move.
            case CastlingMove castlingMove:
                BoardManager.Instance.CastleRook(castlingMove.RookSquare, castlingMove.GetRookEndSquare());
                return true;
            // Handle en passant move.
            case EnPassantMove enPassantMove:
                BoardManager.Instance.TryDestroyVisualPiece(enPassantMove.CapturedPawnSquare);
                return true;
            // Handle promotion move when no promotion piece has been selected yet.
            case PromotionMove { PromotionPiece: null } promotionMove:
                // Activate the promotion UI and disable all pieces.
                UIManager.Instance.SetActivePromotionUI(true);
                BoardManager.Instance.SetActiveAllPieces(false);

                // Cancel any pending promotion UI tasks.
                promotionUITaskCancellationTokenSource?.Cancel();
                promotionUITaskCancellationTokenSource = new CancellationTokenSource();

                // Await user's promotion choice asynchronously.
                ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice,
                    promotionUITaskCancellationTokenSource.Token);

                // Deactivate the promotion UI and re-enable all pieces.
                UIManager.Instance.SetActivePromotionUI(false);
                BoardManager.Instance.SetActiveAllPieces(true);

                // If the task was cancelled, return false.
                if (promotionUITaskCancellationTokenSource == null
                    || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested
                   )
                {
                    return false;
                }

                // Set the chosen promotion piece.
                promotionMove.SetPromotionPiece(
                    PromotionUtil.GeneratePromotionPiece(choice, SideToMove)
                );
                // Update the board visuals for the promotion.
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                promotionUITaskCancellationTokenSource = null;
                return true;
            // Handle promotion move when the promotion piece is already set.
            case PromotionMove promotionMove:
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                return true;
            // Default case: if the special move is not recognised.
            default:
                return false;
        }
    }

    /// <summary>
    /// Blocks until the user selects a piece for pawn promotion.
    /// </summary>
    /// <returns>The elected promotion piece chosen by the user.</returns>
    private ElectedPiece GetUserPromotionPieceChoice()
    {
        // Wait until the user selects a promotion piece.
        while (userPromotionChoice == ElectedPiece.None)
        {
        }

        ElectedPiece result = userPromotionChoice;
        // Reset the user promotion choice.
        userPromotionChoice = ElectedPiece.None;
        return result;
    }

    /// <summary>
    /// Allows the user to elect a promotion piece.
    /// </summary>
    /// <param name="choice">The elected promotion piece.</param>
    public void ElectPiece(ElectedPiece choice)
    {
        userPromotionChoice = choice;
    }

    /// <summary>
    /// Determines whether the specified piece has any legal moves.
    /// </summary>
    /// <param name="piece">The chess piece to evaluate.</param>
    /// <returns>True if the piece has at least one legal move; otherwise, false.</returns>
    public bool HasLegalMoves(Piece piece)
    {
        return game.TryGetLegalMovesForPiece(piece, out _);
    }

    public string GetGameCode()
    {
        return gameCodeStr;
    }

    public void SetGameCode(string code)
    {
        gameCodeStr = code;
        gameCode.text = "GAME CODE: " + gameCodeStr;
    }

    void OnGameActiveChanged(bool oldValue, bool newValue)
    {
        if (!IsHost)
            return;

        if (newValue)
        {
            EnableCorrectPieces();
            NewGameStartedEvent?.Invoke();
        }
        else
            BoardManager.Instance.SetActiveAllPieces(false);
    }

    private void OnPlayerTurnChanged(int oldValue, int newValue)
    {
        // Show White/Black's turn if its not the client's turn, else say your turn
        if (NetworkManager.Singleton.LocalClientId != (newValue == 0 ? whitePlayerId.Value : blackPlayerId.Value))
        {
            string playerName = newValue == 0 ? "White" : "Black";
            UIManager.Instance.ShowMessage($"{playerName}'s turn!");
        }
        else
        {
            UIManager.Instance.ShowMessage("Your turn!");
        }

        EnableCorrectPieces();
        GameResetToHalfMoveEvent?.Invoke();
    }

    private void EnableCorrectPieces()
    {
        Side currentSide = currentPlayerTurn.Value == 0 ? Side.White : Side.Black;
        ulong currentPlayerClientId = currentPlayerTurn.Value == 0 ? whitePlayerId.Value : blackPlayerId.Value;
        Debug.Log(
            $"White Player ID: {whitePlayerId.Value}, Black Player ID: {blackPlayerId.Value}, Current Player Client ID: {currentPlayerClientId}");

        if (NetworkManager.Singleton.LocalClientId == currentPlayerClientId)
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(currentSide);
        else
            BoardManager.Instance.SetActiveAllPieces(false);
    }

    // Security issue, but dont have the time to set up the proper security
    [ServerRpc(RequireOwnership = false)]
    void SetClientIDServerRpc(ulong clientId)
    {
        // Prevent a migrated host (playing as black) from overwriting the white player ID
        if (IsHost && IsPlayingBlack && whitePlayerId.Value != ulong.MaxValue)
        {
            Debug.Log($"Migrated host preventing white player ID overwrite. Keeping ID: {whitePlayerId.Value}");
            return; // Migrated host shouldn't change white player ID if it's already set
        }

        if (whitePlayerId.Value == ulong.MaxValue)
        {
            whitePlayerId.Value = clientId;
            Debug.Log($"White player ID set to: {clientId}");
        }
        else
        {
            blackPlayerId.Value = clientId;
            Debug.Log($"Black player ID set to: {clientId}");
        }
    }

    private void OnPlayerIdsChanged(ulong oldValue, ulong newValue)
    {
        // FIXME: Dirty fix due to unity netcode's handling of defaults, and not having enough time to set up a proper fix
        if (newValue == ulong.MaxValue)
        {
            if (!IsHost)
            {
                SetClientIDServerRpc(NetworkManager.Singleton.LocalClientId);
            }

            return;
        }

        EnableCorrectPieces();
        StartCoroutine(InitialiseAvatar());
    }

    void SetupInitialGameState()
    {
        isGameActive.Value = false;
        currentPlayerTurn.Value = 0;

        // By default, host is white and the first client is black
        whitePlayerId.Value = NetworkManager.Singleton.LocalClientId;

        game = new Game();
    }

    //FIXME: Sometimes the migration doesnt work, either avatar fails, but most commonly it happens if the host migration happens when its blacks turn.
    [ServerRpc(RequireOwnership = true)]
    public void SetupAsMigratedHostServerRpc(string gameState, bool isBlackPlayer)
    {
        // Only the host should execute this
        if (!IsHost)
            return;

        Debug.Log("Setting up as migrated host (black player)");

        if (isBlackPlayer)
        {
            // Set the host as black player
            blackPlayerId.Value = NetworkManager.Singleton.LocalClientId;
            whitePlayerId.Value = ulong.MaxValue; // To be assigned when a client connects
        }

        // Load the migrated game state
        LoadGame(gameState);


        // Set up the black player avatar (host)
        if (FirebaseStorageHandler.Instance != null)
        {
            StartCoroutine(SetupMigratedHostAvatar());
        }

        // Cache the serialized game for future comparisons
        serialisedGame = gameState;

        Debug.Log(
            $"Migrated host setup complete - waiting for client. Current turn: {(currentPlayerTurn.Value == 0 ? "White" : "Black")}");
    }

    IEnumerator SetupMigratedHostAvatar()
    {
        Task<Texture2D> avatarTask = FirebaseStorageHandler.Instance.GetCurrentAvatarTexture();
        yield return new WaitUntil(() => avatarTask.IsCompleted);

        if (avatarTask.Exception == null)
        {
            // Set the host's avatar as black player
            string avatarId = FirebaseStorageHandler.Instance.EquippedAvatarId;
            SetAvatar(avatarTask.Result, false, avatarId);
            blackAvatar.Value = avatarId;
        }
    }


    [ServerRpc(RequireOwnership = true)]
    public void StartGameServerRpc(ulong clientId, bool newGame = false, string existingSerialisedGame = "")
    {
        // Shouldnt happen, but better safe than sorry
        if (!IsHost)
            return;

        if (newGame)
        {
            currentPlayerTurn.Value = 0;
            game = new Game();
            NetworkManagerHandler.Instance.RestartGame();

            if (existingSerialisedGame.Length > 0)
                LoadGame(existingSerialisedGame);
        }

        isGameActive.Value = true;

        string serialisedGameState = SerializeGame();
        serialisedGame = serialisedGameState;

        StartGameClientRpc(serialisedGameState,
            new ClientRpcParams
                { Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { clientId } } });

        leftButtonText.text = "Resign";
        leftButton.onClick.RemoveAllListeners();
        leftButton.onClick.AddListener(Resign);

        rightButtonText.text = "Save Game";
        rightButton.onClick.RemoveAllListeners();
        rightButton.onClick.AddListener(SaveGame);
    }

    [ClientRpc]
    void StartGameClientRpc(string serialisedGameState, ClientRpcParams clientRpcParams = default)
    {
        Instance.LoadGame(serialisedGameState);

        // Set up client buttons when the game starts
        leftButtonText.text = "Resign";
        leftButton.onClick.RemoveAllListeners();
        leftButton.onClick.AddListener(Resign);

        // Add save game functionality for clients
        rightButtonText.text = "Save Game";
        rightButton.onClick.RemoveAllListeners();
        rightButton.onClick.AddListener(SaveGame);

        // Show White/Black's turn if its not the client's turn, else say your turn
        if (NetworkManager.Singleton.LocalClientId !=
            (currentPlayerTurn.Value == 0 ? whitePlayerId.Value : blackPlayerId.Value))
        {
            string playerName = currentPlayerTurn.Value == 0 ? "White" : "Black";
            UIManager.Instance.ShowMessage($"{playerName}'s turn!");
        }
        else
        {
            UIManager.Instance.ShowMessage("Your turn!");
        }

        EnableCorrectPieces();
    }

    /// <summary>
    /// Handle player disconnection
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    public void HandlePlayerDisconnectServerRpc(ulong clientId)
    {
        leftButtonText.text = "Quit";
        leftButton.onClick.RemoveAllListeners();
        leftButton.onClick.AddListener(Quit);

        if (!isGameActive.Value)
            return;

        // Pause the game
        isGameActive.Value = false;

        // Notify clients about the disconnection
        PlayerDisconnectedClientRpc(clientId);
    }

    /// <summary>
    /// Notify clients about player disconnection
    /// </summary>
    [ClientRpc]
    private void PlayerDisconnectedClientRpc(ulong clientId)
    {
        UIManager.Instance.ShowMessage($"Player {clientId} disconnected. Game paused.");
        Debug.Log($"Player {clientId} disconnected. Game paused.");
    }

    async void OnNetworkedPieceMoved(Square startSquare, Transform movedPieceTransform,
        Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        // Don't process moves if game isn't active
        if (!isGameActive.Value)
        {
            // Return piece to its original position
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        // Make sure the current board is in sync with the game
        if (serialisedGame != null && SerializeGame() != serialisedGame)
        {
            // Dont let the player move since its a previous state
            return;
        }

        // Debug player roles
        Debug.Log(
            $"Local Client ID: {NetworkManager.Singleton.LocalClientId}, White Player ID: {whitePlayerId.Value}, Black Player ID: {blackPlayerId.Value}");
        Debug.Log(
            $"Is Playing White: {IsPlayingWhite}, Is Playing Black: {IsPlayingBlack}, Current Turn: {currentPlayerTurn.Value}");

        // Check if it's this player's turn
        Side currentSide = currentPlayerTurn.Value == 0 ? Side.White : Side.Black;
        bool isLocalPlayersTurn = (currentPlayerTurn.Value == 0 && IsPlayingWhite) ||
                                  (currentPlayerTurn.Value == 1 && IsPlayingBlack);

        if (!isLocalPlayersTurn)
        {
            Debug.Log("Not your turn!");
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        Square endSquare = new Square(closestBoardSquareTransform.name);

        // Attempt to retrieve a legal move from the game logic.
        if (!game.TryGetLegalMove(startSquare, endSquare, out Movement move))
        {
            // If no legal move is found, reset the piece's position.
            movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
            // In debug view, log the legal moves for further analysis.
            Piece movedPiece = CurrentBoard[startSquare];
            game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
            UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
            return;
        }

        if (move is not SpecialMove specialMove || await TryHandleSpecialMoveBehaviourAsync(specialMove))
        {
            if (move is SpecialMove)
            {
                if (move is PromotionMove promotionMove)
                {
                    promotionPiece = promotionMove.PromotionPiece;
                }
            }

            ValidateMoveServerRpc(new SerializedSquare(startSquare.File, startSquare.Rank),
                new SerializedSquare(endSquare.File, endSquare.Rank), GetSerializedGame(),
                (byte)CurrentBoard[startSquare.File, startSquare.Rank].GetPieceType(),
                promotionPiece != null ? (byte)promotionPiece.GetPieceType() : (byte)0);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void ValidateMoveServerRpc(SerializedSquare startSquare, SerializedSquare endSquare, string clientSerialisedGame,
        byte pieceTypeByte,
        byte promotionPieceType = 0, ServerRpcParams rpcParams = default)
    {
        // Again, should not be possible
        if (!isGameActive.Value)
            return;

        if (clientSerialisedGame != GetSerializedGame())
        {
            ResetGameStateClientRpc(GetSerializedGame(), new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                    { TargetClientIds = new List<ulong> { rpcParams.Receive.SenderClientId } }
            });
        }

        ulong clientId = rpcParams.Receive.SenderClientId;

        // Debug player IDs
        Debug.Log(
            $"Move attempt - Client ID: {clientId}, White Player ID: {whitePlayerId.Value}, Black Player ID: {blackPlayerId.Value}, Current Turn: {currentPlayerTurn.Value}");

        // Check if it's the correct player's turn
        bool validPlayer = (currentPlayerTurn.Value == 0 && clientId == whitePlayerId.Value) ||
                           (currentPlayerTurn.Value == 1 && clientId == blackPlayerId.Value);

        if (!validPlayer)
        {
            Debug.Log($"Invalid player trying to move: {clientId}");
            return;
        }

        Square start = new Square(startSquare.File, startSquare.Rank);
        Square end = new Square(endSquare.File, endSquare.Rank);

        PieceType originalPieceType = (PieceType)pieceTypeByte;

        if (CurrentBoard[startSquare.File, startSquare.Rank].GetPieceType() != originalPieceType)
        {
            ResetGameStateClientRpc(GetSerializedGame(), new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                    { TargetClientIds = new List<ulong> { rpcParams.Receive.SenderClientId } }
            });
            return;
        }

        // Send an event to the sender to reset his piece. This is mostly done to prevent cheating by bypassing
        // the game logic done on the client side.
        if (!game.TryGetLegalMove(start, end, out Movement move))
            return;

        if (move is PromotionMove promotionMove && promotionPieceType > 0)
        {
            PieceType pieceType = (PieceType)promotionPieceType;
            Side side = currentPlayerTurn.Value == 0 ? Side.White : Side.Black;

            Piece promotionPiece = pieceType switch
            {
                PieceType.Queen => new Queen(side),
                PieceType.Rook => new Rook(side),
                PieceType.Bishop => new Bishop(side),
                PieceType.Knight => new Knight(side),
                _ => null
            };

            promotionMove.SetPromotionPiece(promotionPiece);
        }

        bool isSpecialMove = move is SpecialMove;
        byte specialMoveType = move is SpecialMove
            ? (byte)((move is CastlingMove) ? 1 :
                (move is EnPassantMove) ? 2 :
                (move is PromotionMove) ? 3 : 0)
            : (byte)0;

        SerializedSquare specialSquare = new SerializedSquare(0, 0);

        if (isSpecialMove)
        {
            switch (specialMoveType)
            {
                case 1:
                    specialSquare = new SerializedSquare(((CastlingMove)move).RookSquare.File,
                        ((CastlingMove)move).RookSquare.Rank);
                    break;
                case 2:
                    specialSquare = new SerializedSquare(((EnPassantMove)move).CapturedPawnSquare.File,
                        ((EnPassantMove)move).CapturedPawnSquare.Rank);
                    break;
            }
        }

        game.TryExecuteMove(move, out Piece capturedPiece);

        if (capturedPiece != null)
            UnityAnalyticsHandler.Instance.RecordPieceCaptured(capturedPiece, move,
                GetGameCode());

        SerialisedMove serialisedMove = new SerialisedMove
        {
            StartSquare = startSquare,
            EndSquare = endSquare,
            SpecialSquare = specialSquare,
            IsSpecialMove = isSpecialMove,
            SpecialMoveType = specialMoveType,
            PromotionPieceType = promotionPieceType
        };

        ExecuteMoveClientRpc(
            serialisedMove
        );

        game.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        bool gameEnded = latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate;

        if (gameEnded)
        {
            GameEndClientRpc(latestHalfMove.CausedCheckmate, false, currentPlayerTurn.Value == 0);
            UnityAnalyticsHandler.Instance.RecordVictory(currentPlayerTurn.Value == 0,
                latestHalfMove.CausedCheckmate ? VictoryType.Checkmate : VictoryType.Stalemate, gameCodeStr);
        }
        else
        {
            currentPlayerTurn.Value = currentPlayerTurn.Value == 0 ? 1 : 0;
        }

        MoveExecutedEvent?.Invoke();

        serialisedGame = SerializeGame();
    }

    [ClientRpc]
    void ResetGameStateClientRpc(string serialisedGameState, ClientRpcParams clientRpcParams = default)
    {
        LoadGame(serialisedGameState);
    }

    [ClientRpc]
    void ExecuteMoveClientRpc(SerialisedMove move)
    {
        // Host gamestate would be up to date due to server rpc
        if (IsHost)
            return;

        // Debug info
        Debug.Log(
            $"Executing move on client. Local Client ID: {NetworkManager.Singleton.LocalClientId}, Current Turn Player ID: {(currentPlayerTurn.Value == 0 ? whitePlayerId.Value : blackPlayerId.Value)}");

        Square startSquare = new Square(move.StartSquare.File, move.StartSquare.Rank);
        Square endSquare = new Square(move.EndSquare.File, move.EndSquare.Rank);

        GameObject pieceGo = BoardManager.Instance.GetPieceGOAtPosition(startSquare);

        if (pieceGo == null)
            return;

        Movement movement = null;

        if (move.IsSpecialMove)
        {
            movement = move.SpecialMoveType switch
            {
                1 => new CastlingMove(startSquare, endSquare,
                    new Square(move.SpecialSquare.File, move.SpecialSquare.Rank)),
                2 => new EnPassantMove(startSquare, endSquare,
                    new Square(move.SpecialSquare.File, move.SpecialSquare.Rank)),
                3 => new PromotionMove(startSquare, endSquare),
                _ => null
            };

            if (movement is PromotionMove promotionMove)
            {
                Piece promotionPiece = move.PromotionPieceType switch
                {
                    (byte)PieceType.Queen => new Queen(SideToMove),
                    (byte)PieceType.Rook => new Rook(SideToMove),
                    (byte)PieceType.Bishop => new Bishop(SideToMove),
                    (byte)PieceType.Knight => new Knight(SideToMove),
                    _ => null
                };

                if (promotionPiece == null)
                {
                    Debug.LogError("Promotion piece is null");
                }

                promotionMove.SetPromotionPiece(promotionPiece);
                movement = promotionMove;
            }
        }
        else
        {
            movement = new Movement(startSquare, endSquare);
        }

        game.TryExecuteMove(movement, out Piece _, true);

        MoveExecutedEvent?.Invoke();

        serialisedGame = SerializeGame();
    }

    [ClientRpc]
    void GameEndClientRpc(bool isCheckMate = false, bool isResignation = false, bool didWhiteWin = false)
    {
        // TODO: THIS IS A TEMPFIX AS SOMEHOW EVEN WITH THE SAME CODE, THE OLD SOLUTION DOESNT WORK
        // Reload the latest half move when the game ends
        int latestHalfMoveIndex = game.HalfMoveTimeline.HeadIndex;
        game.ResetGameToHalfMoveIndex(latestHalfMoveIndex);
        GameResetToHalfMoveEvent?.Invoke();
        
        BoardManager.Instance.SetActiveAllPieces(false);

        if (isResignation)
        {
            string winner = didWhiteWin ? "White" : "Black";
            string loser = didWhiteWin ? "Black" : "White";
            UIManager.Instance.ShowMessage($"{loser} Resigned! {winner} wins!");
        }
        else if (isCheckMate)
        {
            string winner = currentPlayerTurn.Value == 0 ? "Black" : "White";
            UIManager.Instance.ShowMessage($"Checkmate! {winner} wins!");
        }
        else
            UIManager.Instance.ShowMessage("Stalemate! The game is a draw.");

        GameEndedEvent?.Invoke();

        if (IsHost)
        {
            isGameActive.Value = false;
            NetworkManagerHandler.Instance.GameOver();

            // Find the other player
            ulong otherClientID = IsPlayingWhite ? blackPlayerId.Value : whitePlayerId.Value;

            leftButtonText.text = "New Game";
            leftButton.onClick.RemoveAllListeners();
            leftButton.onClick.AddListener(() => { StartGameServerRpc(otherClientID, true); });

            rightButtonText.text = "Load Game";
            rightButton.onClick.RemoveAllListeners();
            rightButton.onClick.AddListener(() =>
            {
                NetworkManagerHandler.Instance.LoadGame(inputField.text).ContinueWith(
                    task =>
                    {
                        MainThreadDispatcher.Instance.Enqueue(() =>
                        {
                            StartGameServerRpc(otherClientID, true, task.Result);
                        });
                    });
            });
        }
        else
        {
            leftButtonText.text = "Quit";
            leftButton.onClick.RemoveAllListeners();
            leftButton.onClick.AddListener(Quit);
        }
    }

    public override void OnDestroy()
    {
        isGameActive.OnValueChanged -= OnGameActiveChanged;
        currentPlayerTurn.OnValueChanged -= OnPlayerTurnChanged;
        whitePlayerId.OnValueChanged -= OnPlayerIdsChanged;
        blackPlayerId.OnValueChanged -= OnPlayerIdsChanged;
        whiteAvatar.OnValueChanged -= OnWhiteAvatarChanged;
        blackAvatar.OnValueChanged -= OnBlackAvatarChanged;

        VisualPiece.VisualPieceMoved -= OnNetworkedPieceMoved;
        base.OnDestroy();
    }

    public string GetSerializedGame()
    {
        return serialisedGame ?? SerializeGame();
    }

    public void ResumeGame()
    {
        if (!IsHost)
            return;

        isGameActive.Value = true;

        leftButtonText.text = "Resign";
        leftButton.onClick.RemoveAllListeners();
        leftButton.onClick.AddListener(Resign);
    }

    [ServerRpc(RequireOwnership = true)]
    public void SendGameStateServerRpc(ulong clientId)
    {
        ReceiveGameStateClientRpc(GetSerializedGame(),
            new ClientRpcParams
                { Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { clientId } } });
    }

    [ClientRpc]
    void ReceiveGameStateClientRpc(string serialisedGameState, ClientRpcParams clientRpcParams = default)
    {
        LoadGame(serialisedGameState);
        Debug.Log($"Client ID {NetworkManager.Singleton.LocalClientId} received game state");
    }

    public void EndGameForHostMigration()
    {
        BoardManager.Instance.SetActiveAllPieces(false);
    }

    IEnumerator InitialiseAvatar()
    {
        if (FirebaseStorageHandler.Instance == null)
            throw new Exception("FirebaseStorageHandler is null");

        Task<Texture2D> avatarTask = FirebaseStorageHandler.Instance.GetCurrentAvatarTexture();
        yield return new WaitUntil(() => avatarTask.IsCompleted);

        if (avatarTask.IsFaulted)
            throw new Exception("Failed to load avatar texture");

        string avatarID = FirebaseStorageHandler.Instance.EquippedAvatarId;
        SetAvatar(avatarTask.Result, IsPlayingWhite, avatarID);
    }

    private async Task LoadRemoteAvatar(string avatarId, bool isWhite)
    {
        if (FirebaseStorageHandler.Instance == null)
            return;

        try
        {
            Texture2D avatarTexture = await FirebaseStorageHandler.Instance.GetAvatarTexture(avatarId);

            // Update the UI without triggering another network update
            if (isWhite)
                white_avatar.sprite = Sprite.Create(avatarTexture,
                    new Rect(0, 0, avatarTexture.width, avatarTexture.height),
                    new Vector2(0.5f, 0.5f));
            else
                black_avatar.sprite = Sprite.Create(avatarTexture,
                    new Rect(0, 0, avatarTexture.width, avatarTexture.height),
                    new Vector2(0.5f, 0.5f));
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading remote avatar {avatarId}: {e.Message}");
        }
    }

    private void OnWhiteAvatarChanged(FixedString128Bytes oldValue, FixedString128Bytes newValue)
    {
        string stringNewValue = newValue.ToString();
        if (string.IsNullOrEmpty(stringNewValue))
            return;

        if (!IsPlayingWhite) // Only load opponent's avatar
        {
            Task loadTask = LoadRemoteAvatar(stringNewValue, true);
        }
    }

    private void OnBlackAvatarChanged(FixedString128Bytes oldValue, FixedString128Bytes newValue)
    {
        string stringNewValue = newValue.ToString();
        if (string.IsNullOrEmpty(stringNewValue))
            return;

        if (!IsPlayingBlack) // Only load opponent's avatar
        {
            Task loadTask = LoadRemoteAvatar(stringNewValue, false);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void SetAvatarServerRpc(string avatarURI, bool isWhite)
    {
        if (isWhite)
        {
            whiteAvatar.Value = avatarURI;
            return;
        }

        blackAvatar.Value = avatarURI;
    }

    public void SetAvatar(Texture2D avatar, bool isWhite, string avatarURI = null)
    {
        if (isWhite)
            white_avatar.sprite = Sprite.Create(avatar, new Rect(0, 0, avatar.width, avatar.height),
                new Vector2(0.5f, 0.5f));
        else
            black_avatar.sprite = Sprite.Create(avatar, new Rect(0, 0, avatar.width, avatar.height),
                new Vector2(0.5f, 0.5f));

        if (avatarURI != null)
            SetAvatarServerRpc(avatarURI, isWhite);
    }
}