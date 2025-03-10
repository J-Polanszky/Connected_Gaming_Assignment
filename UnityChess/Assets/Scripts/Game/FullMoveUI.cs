using UnityChess;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the user interface for displaying a full move (both White and Black moves) in the chess game.
/// This includes move numbers, move texts, analysis data, and highlighting of the current move.
/// </summary>
public class FullMoveUI : MonoBehaviour {
	// --- Moves UI Elements ---
	[Header("Moves")]
	// Text element showing the move number (e.g. "1.")
	public Text MoveNumberText;
	// Text element displaying White's move in algebraic notation.
	public Text WhiteMoveText;
	// Text element displaying Black's move in algebraic notation.
	public Text BlackMoveText;
	// Button that can be clicked to reset the board to White's move.
	public Button WhiteMoveButton;
	// Button that can be clicked to reset the board to Black's move.
	public Button BlackMoveButton;
	
	// --- Analysis UI Elements ---
	[Header("Analysis")]
	// Text element displaying analysis for White's move.
	public Text WhiteAnalysisText;
	// Text element displaying analysis for Black's move.
	public Text BlackAnalysisText;
	// Image that visually represents analysis data for White (e.g. progress bar).
	public Image WhiteAnalysisFillImage;
	// Image that visually represents analysis data for Black.
	public Image BlackAnalysisFillImage;

	// --- Colored UI Elements ---
	[Header("Colored Images")]
	// Background image for the move UI.
	public Image backgroundImage;
	// Image used for the White move button.
	public Image whiteMoveButtonImage;
	// Image used for the Black move button.
	public Image blackMoveButtonImage;
	// GameObject used to highlight the current White move.
	public GameObject whiteMoveHighlight;
	// GameObject used to highlight the current Black move.
	public GameObject blackMoveHighlight;

	/// <summary>
	/// Returns the full move number corresponding to this UI element.
	/// It is calculated based on the sibling index (position in the hierarchy) plus one.
	/// </summary>
	public int FullMoveNumber => transform.GetSiblingIndex() + 1;

	/// <summary>
	/// Computes an offset based on the starting side of the game.
	/// If the starting side is White, the offset is 0; otherwise, it is -1.
	/// This is used to correctly calculate half-move indices.
	/// </summary>
	private static int startingSideOffset => GameManager.Instance.StartingSide switch {
		Side.White => 0,
		_ => -1
	};

	/// <summary>
	/// Calculates the half-move index for White's move based on the UI element's sibling index.
	/// </summary>
	private int WhiteHalfMoveIndex => transform.GetSiblingIndex() * 2 + startingSideOffset;
	
	/// <summary>
	/// Calculates the half-move index for Black's move based on the UI element's sibling index.
	/// </summary>
	private int BlackHalfMoveIndex => transform.GetSiblingIndex() * 2 + 1 + startingSideOffset;

	/// <summary>
	/// Initialises the UI element when the script is loaded.
	/// Subscribes to game events and validates the move highlights.
	/// </summary>
	private void Start() {
		// Validate and update move highlights at startup.
		ValidateMoveHighlights();

		// Subscribe to events to update highlights when moves are executed or the game is reset.
		GameManager.MoveExecutedEvent += ValidateMoveHighlights;
		GameManager.GameResetToHalfMoveEvent += ValidateMoveHighlights;
	}

	/// <summary>
	/// Unsubscribes from game events when the UI element is destroyed.
	/// </summary>
	private void OnDestroy() {
		GameManager.MoveExecutedEvent -= ValidateMoveHighlights;
		GameManager.GameResetToHalfMoveEvent -= ValidateMoveHighlights;
	}

	/// <summary>
	/// Adjusts the colour of the background and move button images by darkening them.
	/// This method is used to create an alternate colour scheme.
	/// </summary>
	/// <param name="darkenAmount">The amount by which to darken the colour channels.</param>
	public void SetAlternateColor(float darkenAmount) {
		// Iterate over the specified images.
		foreach (Image image in new []{ backgroundImage, whiteMoveButtonImage, blackMoveButtonImage }) {
			// Get the current colour of the image.
			Color lightColor = image.color;
			// Apply the darkening amount to each colour channel.
			image.color = new Color(lightColor.r - darkenAmount, lightColor.g - darkenAmount, lightColor.b - darkenAmount);
		}
	}

	/// <summary>
	/// Resets the board to the state corresponding to White's move for this full move.
	/// </summary>
	public void ResetBoardToWhiteMove() => GameManager.Instance.ResetGameToHalfMoveIndex(WhiteHalfMoveIndex);

	/// <summary>
	/// Resets the board to the state corresponding to Black's move for this full move.
	/// </summary>
	public void ResetBoardToBlackMove() => GameManager.Instance.ResetGameToHalfMoveIndex(BlackHalfMoveIndex);

	/// <summary>
	/// Validates and updates the move highlight indicators.
	/// Activates the highlight for the move that corresponds to the latest half-move index.
	/// </summary>
	private void ValidateMoveHighlights() {
		// Get the latest half-move index from the game manager.
		int latestHalfMoveIndex = GameManager.Instance.LatestHalfMoveIndex;
		// Activate the White move highlight if it matches the current half-move index.
		whiteMoveHighlight.SetActive(latestHalfMoveIndex == WhiteHalfMoveIndex);
		// Activate the Black move highlight if it matches the current half-move index.
		blackMoveHighlight.SetActive(latestHalfMoveIndex == BlackHalfMoveIndex);
	}
}
