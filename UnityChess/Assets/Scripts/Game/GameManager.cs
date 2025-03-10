using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityEngine;

/// <summary>
/// Manages the overall game state, including game start, moves execution,
/// special moves handling (such as castling, en passant, and promotion), and game reset.
/// Inherits from a singleton base class to ensure a single instance throughout the application.
/// </summary>
public class GameManager : MonoBehaviourSingleton<GameManager> {
	// Events signalling various game state changes.
	public static event Action NewGameStartedEvent;
	public static event Action GameEndedEvent;
	public static event Action GameResetToHalfMoveEvent;
	public static event Action MoveExecutedEvent;
	
	/// <summary>
	/// Gets the current board state from the game.
	/// </summary>
	public Board CurrentBoard {
		get {
			// Attempts to retrieve the current board from the board timeline.
			game.BoardTimeline.TryGetCurrent(out Board currentBoard);
			return currentBoard;
		}
	}

	/// <summary>
	/// Gets the side (White/Black) whose turn it is to move.
	/// </summary>
	public Side SideToMove {
		get {
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
	public int FullMoveNumber => StartingSide switch {
		Side.White => LatestHalfMoveIndex / 2 + 1,
		Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
		_ => -1
	};

	private bool isWhiteAI;
	private bool isBlackAI;

	/// <summary>
	/// Gets a list of all current pieces on the board, along with their positions.
	/// </summary>
	public List<(Square, Piece)> CurrentPieces {
		get {
			// Clear the backing list before populating with current pieces.
			currentPiecesBacking.Clear();
			// Iterate over every square on the board.
			for (int file = 1; file <= 8; file++) {
				for (int rank = 1; rank <= 8; rank++) {
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
	public void Start() {
		// Subscribe to the event triggered when a visual piece is moved.
		VisualPiece.VisualPieceMoved += OnPieceMoved;

		// Initialise the serializers for FEN and PGN formats.
		serializersByType = new Dictionary<GameSerializationType, IGameSerializer> {
			[GameSerializationType.FEN] = new FENSerializer(),
			[GameSerializationType.PGN] = new PGNSerializer()
		};
		
		// Begin a new game.
		StartNewGame();
		
#if DEBUG_VIEW
		// Enable debug view if compiled with DEBUG_VIEW flag.
		unityChessDebug.gameObject.SetActive(true);
		unityChessDebug.enabled = true;
#endif
	}
	
	/// <summary>
	/// Starts a new game by creating a new game instance and invoking the NewGameStartedEvent.
	/// </summary>
	public async void StartNewGame() {
		game = new Game();
		NewGameStartedEvent?.Invoke();
	}

	/// <summary>
	/// Serialises the current game state using the selected serialization format.
	/// </summary>
	/// <returns>A string representing the serialised game state.</returns>
	public string SerializeGame() {
		return serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer)
			? serializer?.Serialize(game)
			: null;
	}
	
	/// <summary>
	/// Loads a game from the given serialised game state string.
	/// </summary>
	/// <param name="serializedGame">The serialised game state string.</param>
	public void LoadGame(string serializedGame) {
		game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
		NewGameStartedEvent?.Invoke();
	}

	/// <summary>
	/// Resets the game to a specific half-move index.
	/// </summary>
	/// <param name="halfMoveIndex">The target half-move index to reset the game to.</param>
	public void ResetGameToHalfMoveIndex(int halfMoveIndex) {
		// If the reset operation fails, exit early.
		if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;
		
		// Disable promotion UI and cancel any pending promotion tasks.
		UIManager.Instance.SetActivePromotionUI(false);
		promotionUITaskCancellationTokenSource?.Cancel();
		// Notify subscribers that the game has been reset to a half-move.
		GameResetToHalfMoveEvent?.Invoke();
	}

	/// <summary>
	/// Attempts to execute a given move in the game.
	/// </summary>
	/// <param name="move">The move to execute.</param>
	/// <returns>True if the move was successfully executed; otherwise, false.</returns>
	private bool TryExecuteMove(Movement move) {
		// Attempt to execute the move within the game logic.
		if (!game.TryExecuteMove(move)) {
			return false;
		}

		// Retrieve the latest half-move from the timeline.
		HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
		
		// If the latest move resulted in checkmate or stalemate, disable further moves.
		if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate) {
			BoardManager.Instance.SetActiveAllPieces(false);
			GameEndedEvent?.Invoke();
		} else {
			// Otherwise, ensure that only the pieces of the side to move are enabled.
			BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
		}

		// Signal that a move has been executed.
		MoveExecutedEvent?.Invoke();

		return true;
	}
	
	/// <summary>
	/// Handles special move behaviour asynchronously (castling, en passant, and promotion).
	/// </summary>
	/// <param name="specialMove">The special move to process.</param>
	/// <returns>A task that resolves to true if the special move was handled; otherwise, false.</returns>
	private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove) {
		switch (specialMove) {
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
				ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);
				
				// Deactivate the promotion UI and re-enable all pieces.
				UIManager.Instance.SetActivePromotionUI(false);
				BoardManager.Instance.SetActiveAllPieces(true);

				// If the task was cancelled, return false.
				if (promotionUITaskCancellationTokenSource == null
				    || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested
				) { return false; }

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
	private ElectedPiece GetUserPromotionPieceChoice() {
		// Wait until the user selects a promotion piece.
		while (userPromotionChoice == ElectedPiece.None) { }

		ElectedPiece result = userPromotionChoice;
		// Reset the user promotion choice.
		userPromotionChoice = ElectedPiece.None;
		return result;
	}
	
	/// <summary>
	/// Allows the user to elect a promotion piece.
	/// </summary>
	/// <param name="choice">The elected promotion piece.</param>
	public void ElectPiece(ElectedPiece choice) {
		userPromotionChoice = choice;
	}

	/// <summary>
	/// Handles the event triggered when a visual chess piece is moved.
	/// This method validates the move, handles special moves, and updates the board state.
	/// </summary>
	/// <param name="movedPieceInitialSquare">The original square of the moved piece.</param>
	/// <param name="movedPieceTransform">The transform of the moved piece.</param>
	/// <param name="closestBoardSquareTransform">The transform of the closest board square.</param>
	/// <param name="promotionPiece">Optional promotion piece (used in pawn promotion).</param>
	private async void OnPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null) {
		// Determine the destination square based on the name of the closest board square transform.
		Square endSquare = new Square(closestBoardSquareTransform.name);

		// Attempt to retrieve a legal move from the game logic.
		if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move)) {
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

		// If the move is a promotion move, set the promotion piece.
		if (move is PromotionMove promotionMove) {
			promotionMove.SetPromotionPiece(promotionPiece);
		}

		// If the move is not a special move or its special behaviour is successfully handled,
		// and the move executes successfully...
		if ((move is not SpecialMove specialMove || await TryHandleSpecialMoveBehaviourAsync(specialMove))
		    && TryExecuteMove(move)
		) {
			// For non-special moves, update the board visuals by destroying any piece at the destination.
			if (move is not SpecialMove) { BoardManager.Instance.TryDestroyVisualPiece(move.End); }

			// For promotion moves, update the moved piece transform to the newly created visual piece.
			if (move is PromotionMove) {
				movedPieceTransform = BoardManager.Instance.GetPieceGOAtPosition(move.End).transform;
			}

			// Re-parent the moved piece to the destination square and update its position.
			movedPieceTransform.parent = closestBoardSquareTransform;
			movedPieceTransform.position = closestBoardSquareTransform.position;
		}
	}
	
	/// <summary>
	/// Determines whether the specified piece has any legal moves.
	/// </summary>
	/// <param name="piece">The chess piece to evaluate.</param>
	/// <returns>True if the piece has at least one legal move; otherwise, false.</returns>
	public bool HasLegalMoves(Piece piece) {
		return game.TryGetLegalMovesForPiece(piece, out _);
	}
}
