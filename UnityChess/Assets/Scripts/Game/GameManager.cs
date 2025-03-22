using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityChess;
using UnityEngine;

/// <summary>
/// Manages the overall game state, including game start, moves execution,
/// special moves handling (such as castling, en passant, and promotion), and game reset.
/// Inherits from a singleton base class to ensure a single instance throughout the application.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    private NetworkVariable<bool> isGameActive = new(false);
    private NetworkVariable<int> currentPlayerTurn = new(0); // 0 = White, 1 = Black

    private Transform gameInfo;
    private string gameCodeStr;
    private TextMeshProUGUI gameCode;

    // Events signalling various game state changes.
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    public static event Action MoveExecutedEvent;


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

        // // Subscribe to the event triggered when a visual piece is moved.
        // VisualPiece.VisualPieceMoved += OnPieceMoved;
        //
        // // Initialise the serializers for FEN and PGN formats.
        // serializersByType = new Dictionary<GameSerializationType, IGameSerializer> {
        // 	[GameSerializationType.FEN] = new FENSerializer(),
        // 	[GameSerializationType.PGN] = new PGNSerializer()
        // };
        //
        // // Begin a new game.
        // StartNewGame();

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

    /// <summary>
    /// Loads a game from the given serialised game state string.
    /// </summary>
    /// <param name="serializedGame">The serialised game state string.</param>
    public void LoadGame(string serializedGame)
    {
        game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
        NewGameStartedEvent?.Invoke();
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

    public void SetGameCode(string code)
    {
        gameCodeStr = code;
        gameCode.text = "GAME CODE: " + gameCodeStr;
    }

    void OnGameActiveChanged(bool oldValue, bool newValue)
    {
        if(!IsHost)
            return;
        
        if (newValue)
        {
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(IsHost ? Side.White : Side.Black);

            NewGameStartedEvent?.Invoke();
        }
        else
            BoardManager.Instance.SetActiveAllPieces(false);
    }

    private void OnPlayerTurnChanged(int oldValue, int newValue)
    {
        Side sideToMove = newValue == 0 ? Side.White : Side.Black;

        if ((IsHost && sideToMove == Side.White) || (!IsHost && sideToMove == Side.Black))
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(sideToMove);
        else
            BoardManager.Instance.SetActiveAllPieces(false);

        GameResetToHalfMoveEvent?.Invoke();
    }

    public override void OnNetworkSpawn()
    {
        isGameActive.OnValueChanged += OnGameActiveChanged;
        currentPlayerTurn.OnValueChanged += OnPlayerTurnChanged;

        // Subscribe to the event triggered when a visual piece is moved.
        VisualPiece.VisualPieceMoved += OnNetworkedPieceMoved;

        // Initialise the serializers for FEN and PGN formats.
        serializersByType = new Dictionary<GameSerializationType, IGameSerializer>
        {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };

        if (IsHost)
            SetupInitialGameState();
    }

    void SetupInitialGameState()
    {
        isGameActive.Value = false;
        currentPlayerTurn.Value = 0;
        game = new Game();
    }

    [ServerRpc(RequireOwnership = true)]
    public void StartGameServerRpc(ulong clientId)
    {
        if (!IsHost)
            return;

        isGameActive.Value = true;
        
        string serialisedGameState = SerializeGame();

        StartGameClientRpc(serialisedGameState,
            new ClientRpcParams
                { Send = new ClientRpcSendParams { TargetClientIds = new List<ulong> { { clientId } } } });
    }

    [ClientRpc]
    void StartGameClientRpc(string serialisedGameState, ClientRpcParams clientRpcParams = default)
    {
        Instance.LoadGame(serialisedGameState);
    }

    /// <summary>
    /// Handle player disconnection
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    public void HandlePlayerDisconnectServerRpc(ulong clientId)
    {
        if (!IsHost) return;

        // Pause the game or end it
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

    void OnNetworkedPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform,
        Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        // Don't process moves if game isn't active
        // Should not be possible
        if (!isGameActive.Value)
        {
            // Return piece to its original position
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        bool canMove = (currentPlayerTurn.Value == 0 && IsHost) || (currentPlayerTurn.Value == 1 && !IsHost);

        if (!canMove)
        {
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        Square endSquare = new Square(closestBoardSquareTransform.name);

        // Attempt to retrieve a legal move from the game logic.
        if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move))
        {
            // If no legal move is found, reset the piece's position.
            movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
			// In debug view, log the legal moves for further analysis.
			Piece movedPiece = CurrentBoard[movedPieceInitialSquare];
			game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
			UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
            return;
        }

        ValidateMoveServerRpc(new SerializedSquare(movedPieceInitialSquare.File, movedPieceInitialSquare.Rank),
            new SerializedSquare(endSquare.File, endSquare.Rank),
            promotionPiece != null ? (byte)promotionPiece.GetPieceType() : (byte)0);
    }

    [ServerRpc(RequireOwnership = false)]
    void ValidateMoveServerRpc(SerializedSquare startSquare, SerializedSquare endSquare,
        byte promotionPieceType = 0, ServerRpcParams rpcParams = default)
    {
        // Again, should not be possible
        if (!isGameActive.Value)
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;

        bool validPlayer = (currentPlayerTurn.Value == 0 && clientId == 0) ||
                           (currentPlayerTurn.Value == 1 && clientId != 0);

        if (!validPlayer)
            return;

        Square start = new Square(startSquare.File, startSquare.Rank);
        Square end = new Square(endSquare.File, endSquare.Rank);

        // Send an event to the sender to reset his piece. This is mostly done to prevent cheating by bypassing
        // the game logic done on the client side.
        if (!game.TryGetLegalMove(start, end, out Movement move))
            return;

        if (move is PromotionMove promotionMove && promotionPieceType > 0)
        {
            PieceType pieceType = (PieceType)promotionPieceType;
            Side side = (currentPlayerTurn.Value == 0 && clientId == 0) ? Side.White : Side.Black;

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

        if (!game.TryExecuteMove(move)) //FIXME: This will likely break the game
            return;

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

        ExecuteMoveClientRpc(
            new SerialisedMove()
            {
                StartSquare = startSquare,
                EndSquare = endSquare,
                SpecialSquare = specialSquare,
                IsSpecialMove = isSpecialMove,
                SpecialMoveType = specialMoveType,
                PromotionPieceType = promotionPieceType
            }
        );

        game.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        bool gameEnded = latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate;

        if (gameEnded)
            GameEndClientRpc(latestHalfMove.CausedCheckmate);
        else
            currentPlayerTurn.Value = currentPlayerTurn.Value == 0 ? 1 : 0;

        MoveExecutedEvent?.Invoke();
    }

    [ClientRpc]
    void ExecuteMoveClientRpc(SerialisedMove move)
    {
        Debug.Log("Client ID: " + NetworkManager.Singleton.LocalClientId);
        
        Square startSquare = new Square(move.StartSquare.File, move.StartSquare.Rank);
        Square endSquare = new Square(move.EndSquare.File, move.EndSquare.Rank);

        GameObject pieceGo = BoardManager.Instance.GetPieceGOAtPosition(startSquare);

        if (pieceGo == null)
            return;

        Transform pieceTransform = pieceGo.transform;
        Transform destTransform = BoardManager.Instance.GetSquareGOByPosition(endSquare).transform;

        if (move.IsSpecialMove)
        {
            SpecialMove specialMove = move.SpecialMoveType switch
            {
                // Castle takes king square, end square and rook square
                // En passant takes attacking pawn position, end, and captured pawn position
                // Promotion takes pawn position and end position
                // The king square, attacking pawn and the pawn position for these 3 would be start square no?
                1 => new CastlingMove(startSquare, endSquare,
                    new Square(move.SpecialSquare.File, move.SpecialSquare.Rank)),
                2 => new EnPassantMove(startSquare, endSquare,
                    new Square(move.SpecialSquare.File, move.SpecialSquare.Rank)),
                3 => new PromotionMove(startSquare, endSquare),
                _ => null
            };

            TryHandleSpecialMoveBehaviourAsync(specialMove).Wait();
        }

        BoardManager.Instance.TryDestroyVisualPiece(endSquare);
        
        pieceTransform.SetParent(destTransform);
        pieceTransform.position = destTransform.position;
        
        game.TryExecuteMove(new Movement(startSquare, endSquare));
        
        // Update the current board state
        // Piece movedPiece = Instance.CurrentBoard[startSquare.File, startSquare.Rank];
        // Instance.CurrentBoard[startSquare.File, startSquare.Rank] = null;
        // Instance.CurrentBoard[endSquare.File, endSquare.Rank] = movedPiece;
    }

    [ClientRpc]
    void GameEndClientRpc(bool isCheckMate)
    {
        BoardManager.Instance.SetActiveAllPieces(false);

        if (isCheckMate)
        {
            string winner = currentPlayerTurn.Value == 0 ? "Black" : "White";
            UIManager.Instance.ShowMessage($"Checkmate! {winner} wins!");
        }
        else
            UIManager.Instance.ShowMessage("Stalemate! The game is a draw.");

        GameEndedEvent?.Invoke();
    }

    public override void OnDestroy()
    {
        isGameActive.OnValueChanged -= OnGameActiveChanged;
        currentPlayerTurn.OnValueChanged -= OnPlayerTurnChanged;

        base.OnDestroy();

        VisualPiece.VisualPieceMoved -= OnNetworkedPieceMoved;
    }
}