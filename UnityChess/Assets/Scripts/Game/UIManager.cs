using System;
using System.Collections.Generic;
using UnityChess;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the user interface of the chess game, including promotion UI, move history,
/// turn indicators, game string serialization, and board information displays.
/// Inherits from MonoBehaviourSingleton to ensure a single instance throughout the game.
/// </summary>
public class UIManager : MonoBehaviourSingleton<UIManager> {
	// Reference to the promotion UI panel.
	[SerializeField] private GameObject promotionUI = null;
	// Text element to display game result messages (e.g. win, draw).
	[SerializeField] private Text resultText = null;
	// Input field to display and edit the serialized game state string.
	[SerializeField] private InputField GameStringInputField = null;
	// Indicator image for White's turn.
	[SerializeField] private Image whiteTurnIndicator = null;
	// Indicator image for Black's turn.
	[SerializeField] private Image blackTurnIndicator = null;
	// Parent GameObject that holds the move history UI elements.
	[SerializeField] private GameObject moveHistoryContentParent = null;
	// Scrollbar for the move history list.
	[SerializeField] private Scrollbar moveHistoryScrollbar = null;
	// Prefab for the full move UI element.
	[SerializeField] private FullMoveUI moveUIPrefab = null;
	// Array of text elements for displaying board information.
	[SerializeField] private Text[] boardInfoTexts = null;
	// Background colour for the move history UI.
	[SerializeField] private Color backgroundColor = new Color(0.39f, 0.39f, 0.39f);
	// Text colour for the board information.
	[SerializeField] private Color textColor = new Color(1f, 0.71f, 0.18f);
	// Darkening factor for button colours (range -0.25 to 0.25).
	[SerializeField, Range(-0.25f, 0.25f)] private float buttonColorDarkenAmount = 0f;
	// Darkening factor for alternate move history row colours (range -0.25 to 0.25).
	[SerializeField, Range(-0.25f, 0.25f)] private float moveHistoryAlternateColorDarkenAmount = 0f;
	
	// Timeline to keep track of the full move UI elements in sequence.
	private Timeline<FullMoveUI> moveUITimeline;
	// Computed button colour based on the background colour and darkening factor.
	private Color buttonColor;

	/// <summary>
	/// Initialises the UIManager, subscribes to game events, and configures initial UI settings.
	/// </summary>
	private void Start() {
		// Subscribe to various game events.
		GameManager.NewGameStartedEvent += OnNewGameStarted;
		GameManager.GameEndedEvent += OnGameEnded;
		GameManager.MoveExecutedEvent += OnMoveExecuted;
		GameManager.GameResetToHalfMoveEvent += OnGameResetToHalfMove;
		
		// Initialise the timeline for move UI elements.
		moveUITimeline = new Timeline<FullMoveUI>();
		// Set the colour for all board information text elements.
		foreach (Text boardInfoText in boardInfoTexts) {
			boardInfoText.color = textColor;
		}

		// Calculate the button colour based on the background colour and darkening factor.
		buttonColor = new Color(
			backgroundColor.r - buttonColorDarkenAmount, 
			backgroundColor.g - buttonColorDarkenAmount, 
			backgroundColor.b - buttonColorDarkenAmount
		);
	}

	/// <summary>
	/// Handles the event when a new game starts.
	/// Clears previous move history, updates UI fields, and resets result text.
	/// </summary>
	private void OnNewGameStarted() {
		// Update the serialized game string input field.
		UpdateGameStringInputField();
		// Validate turn indicator images.
		ValidateIndicators();
		
		// Clear all child GameObjects under the move history parent.
		for (int i = 0; i < moveHistoryContentParent.transform.childCount; i++) {
			Destroy(moveHistoryContentParent.transform.GetChild(i).gameObject);
		}
		
		// Clear the move UI timeline.
		moveUITimeline.Clear();

		// Hide the result text (game outcome) since the game has just started.
		resultText.gameObject.SetActive(false);
	}

	/// <summary>
	/// Handles the event when the game ends (via checkmate or stalemate).
	/// Displays the game outcome message.
	/// </summary>
	private void OnGameEnded() {
		// Retrieve the latest half-move from the game timeline.
		GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);

		// Set the result text based on whether checkmate or stalemate occurred.
		if (latestHalfMove.CausedCheckmate) {
			resultText.text = $"{latestHalfMove.Piece.Owner} Wins!";
		} else if (latestHalfMove.CausedStalemate) {
			resultText.text = "Draw.";
		}

		// Display the result text.
		resultText.gameObject.SetActive(true);
	}

	/// <summary>
	/// Handles the event when a move is executed.
	/// Updates the game string, turn indicators, and adds the move to the move history UI.
	/// </summary>
	private void OnMoveExecuted() {
		// Update the serialized game string input field.
		UpdateGameStringInputField();
		// Get the side that is now to move.
		Side sideToMove = GameManager.Instance.SideToMove;
		// Enable the appropriate turn indicator.
		whiteTurnIndicator.enabled = sideToMove == Side.White;
		blackTurnIndicator.enabled = sideToMove == Side.Black;

		// Retrieve the latest half-move and add it to the move history UI.
		GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove lastMove);
		AddMoveToHistory(lastMove, sideToMove.Complement());
	}

	/// <summary>
	/// Handles the event when the game is reset to a specific half-move.
	/// Updates the game string and synchronises the move UI timeline.
	/// </summary>
	private void OnGameResetToHalfMove() {
		// Update the serialized game string input field.
		UpdateGameStringInputField();
		// Set the timeline's head index to the current full move number.
		moveUITimeline.HeadIndex = GameManager.Instance.LatestHalfMoveIndex / 2;
		// Validate the turn indicators.
		ValidateIndicators();
	}

	/// <summary>
	/// Activates or deactivates the promotion UI.
	/// </summary>
	/// <param name="value">True to display the promotion UI; false to hide it.</param>
	public void SetActivePromotionUI(bool value) => promotionUI.gameObject.SetActive(value);

	/// <summary>
	/// Processes the user's election choice for a promotion piece.
	/// </summary>
	/// <param name="choice">The integer representation of the chosen promotion piece.</param>
	public void OnElectionButton(int choice) => GameManager.Instance.ElectPiece((ElectedPiece)choice);

	/// <summary>
	/// Resets the game to the very first half-move.
	/// </summary>
	public void ResetGameToFirstHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(0);

	/// <summary>
	/// Resets the game to the previous half-move.
	/// </summary>
	public void ResetGameToPreviousHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(Math.Max(0, GameManager.Instance.LatestHalfMoveIndex - 1));

	/// <summary>
	/// Resets the game to the next half-move.
	/// </summary>
	public void ResetGameToNextHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(Math.Min(GameManager.Instance.LatestHalfMoveIndex + 1, GameManager.Instance.HalfMoveTimeline.Count - 1));

	/// <summary>
	/// Resets the game to the last half-move in the timeline.
	/// </summary>
	public void ResetGameToLastHalfMove() => GameManager.Instance.ResetGameToHalfMoveIndex(GameManager.Instance.HalfMoveTimeline.Count - 1);

	/// <summary>
	/// Starts a new game by invoking the corresponding method in GameManager.
	/// </summary>
	public void StartNewGame() => GameManager.Instance.StartNewGame();
	
	/// <summary>
	/// Loads a game from the text entered in the game string input field.
	/// </summary>
	public void LoadGame() => GameManager.Instance.LoadGame(GameStringInputField.text);

	/// <summary>
	/// Adds a new move to the move history UI based on the latest half-move.
	/// </summary>
	/// <param name="latestHalfMove">The most recent half-move executed.</param>
	/// <param name="latestTurnSide">The side that executed the move (complement of the current turn).</param>
	private void AddMoveToHistory(HalfMove latestHalfMove, Side latestTurnSide) {
		// Remove any alternate history entries if the timeline is not up-to-date.
		RemoveAlternateHistory();
		
		// Process based on the side that made the move.
		switch (latestTurnSide) {
			case Side.Black: {
				// For Black moves, if no full move UI exists yet, instantiate one.
				if (moveUITimeline.HeadIndex == -1) {
					FullMoveUI newFullMoveUI = Instantiate(moveUIPrefab, moveHistoryContentParent.transform);
					moveUITimeline.AddNext(newFullMoveUI);
					
					// Set the new UI element's position based on the current full move number.
					newFullMoveUI.transform.SetSiblingIndex(GameManager.Instance.FullMoveNumber - 1);
					newFullMoveUI.backgroundImage.color = backgroundColor;
					newFullMoveUI.whiteMoveButtonImage.color = buttonColor;
					newFullMoveUI.blackMoveButtonImage.color = buttonColor;
					
					// Apply alternate colour for even-numbered moves.
					if (newFullMoveUI.FullMoveNumber % 2 == 0) {
						newFullMoveUI.SetAlternateColor(moveHistoryAlternateColorDarkenAmount);
					}

					// Set the move number text.
					newFullMoveUI.MoveNumberText.text = $"{newFullMoveUI.FullMoveNumber}.";
					// Disable the White move button for this full move.
					newFullMoveUI.WhiteMoveButton.enabled = false;
				}
				
				// Retrieve the latest full move UI element.
				moveUITimeline.TryGetCurrent(out FullMoveUI latestFullMoveUI);
				// Update the Black move text with the algebraic notation of the latest half-move.
				latestFullMoveUI.BlackMoveText.text = latestHalfMove.ToAlgebraicNotation();
				// Enable the Black move button.
				latestFullMoveUI.BlackMoveButton.enabled = true;
				
				break;
			}
			case Side.White: {
				// For White moves, instantiate a new full move UI element.
				FullMoveUI newFullMoveUI = Instantiate(moveUIPrefab, moveHistoryContentParent.transform);
				newFullMoveUI.transform.SetSiblingIndex(GameManager.Instance.FullMoveNumber - 1);
				newFullMoveUI.backgroundImage.color = backgroundColor;
				newFullMoveUI.whiteMoveButtonImage.color = buttonColor;
				newFullMoveUI.blackMoveButtonImage.color = buttonColor;

				// Apply alternate colour for even-numbered moves.
				if (newFullMoveUI.FullMoveNumber % 2 == 0) {
					newFullMoveUI.SetAlternateColor(moveHistoryAlternateColorDarkenAmount);
				}

				// Set the move number text.
				newFullMoveUI.MoveNumberText.text = $"{newFullMoveUI.FullMoveNumber}.";
				// Update the White move text with the algebraic notation of the latest half-move.
				newFullMoveUI.WhiteMoveText.text = latestHalfMove.ToAlgebraicNotation();
				// Clear the Black move text.
				newFullMoveUI.BlackMoveText.text = "";
				// Disable the Black move button.
				newFullMoveUI.BlackMoveButton.enabled = false;
				// Enable the White move button.
				newFullMoveUI.WhiteMoveButton.enabled = true;
				
				// Add the new full move UI element to the timeline.
				moveUITimeline.AddNext(newFullMoveUI);
				break;
			}
		}

		// Reset the move history scrollbar to the top.
		moveHistoryScrollbar.value = 0;
	}

	/// <summary>
	/// Removes any move history entries that are not part of the current game timeline.
	/// </summary>
	private void RemoveAlternateHistory() {
		// Check if the move UI timeline is not up-to-date.
		if (!moveUITimeline.IsUpToDate) {
			// Retrieve the latest half-move.
			GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove lastHalfMove);
			// If checkmate occurred, activate the result text.
			resultText.gameObject.SetActive(lastHalfMove.CausedCheckmate);
			// Get the list of full move UI elements that diverge from the current timeline.
			List<FullMoveUI> divergentFullMoveUIs = moveUITimeline.PopFuture();
			// Destroy each divergent full move UI element.
			foreach (FullMoveUI divergentFullMoveUI in divergentFullMoveUIs) {
				Destroy(divergentFullMoveUI.gameObject);
			}
		}
	}

	/// <summary>
	/// Validates and updates the turn indicators based on the current side to move.
	/// </summary>
	private void ValidateIndicators() {
		Side sideToMove = GameManager.Instance.SideToMove;
		// Enable the White turn indicator if it is White's turn.
		whiteTurnIndicator.enabled = sideToMove == Side.White;
		// Enable the Black turn indicator if it is Black's turn.
		blackTurnIndicator.enabled = sideToMove == Side.Black;
	}

	/// <summary>
	/// Updates the game string input field with the current serialized game state.
	/// </summary>
	private void UpdateGameStringInputField() => GameStringInputField.text = GameManager.Instance.SerializeGame();
}
