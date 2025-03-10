# UnityChess (Chess Game)

UnityChess is a 3D Chess game built using Unity/C# and adapted for the assignment from: https://github.com/ErkrodC/UnityChess/tree/development.

Debug: Should you wish to view a 2D representation of the game, you can enable the DebugView GameObject. Otherwise, it is not required and not considered part of the game.

Here is some information regarding the classes:

**Board Management**  
The *BoardManager* class is responsible for initialising the chessboard and managing the placement of pieces. In its *Awake()* method, it creates a grid of 64 squares, each represented as a GameObject, and stores them in a dictionary for quick lookup. This class also handles actions such as casting the rook during castling moves and clearing the board when a new game starts or the game state is reset. Furthermore, it provides utility functions to determine the square closest to a moved piece and to update visual elements on the board.

---

**Game Logic and State Management**  
The *GameManager* class orchestrates the game logic. It maintains the current board state, handles move execution, and manages special moves such as castling, en passant, and pawn promotion. The game state is tracked using timelines for half-moves, allowing the game to be reset to previous states. Moreover, the class supports serialization of the game state (using FEN or PGN formats), which is useful for saving and loading games. Events are raised during key game transitions, ensuring that other components, such as the UI, can respond accordingly.

---

**User Interface Handling**  
The *UIManager* class governs the visual feedback provided to the player. It updates turn indicators, move history, and game result texts. The move history is dynamically managed by creating UI elements that represent each full move, and the interface supports resetting the game to any half-move state. In addition, the class manages the display of the promotion user interface, ensuring that the user is able to select the desired piece when a pawn reaches the opposite end of the board.

---

**Piece Interaction and Visual Representation**  
The *VisualPiece* class manages the interactive aspect of chess pieces. It enables the pieces to be dragged and dropped across the board. On release, the class determines the nearest square based on proximity calculations and then triggers an event to notify the game manager of the attempted move. This design ensures that the visual movement of pieces remains in sync with the underlying game logic.

---

**Singleton and Debug Utilities**  
A generic singleton base class, *MonoBehaviourSingleton*, is used to ensure that key classes (such as *BoardManager*, *GameManager*, and *UIManager*) have a single instance throughout the game. Additionally, the *UnityChessDebug* class provides a debug view of the chessboard by updating text and colour information for each square. This facilitates easier troubleshooting and visual verification of the board state during development.

---

# UnityChessLib (Chess Engine)

## Important: These scripts are powering the chess engine and are of less importance for the task at hand. Please do not modify these scripts unless absolutely necessary.

**AI Components**

In the *UnityChess.AI* namespace, two classes are defined:

- **AssessedMove**: This class encapsulates a chess move alongside an evaluation value. It serves as a data container that pairs a potential move with its corresponding score, which is likely used in decision-making processes within the chess AI.

- **TreeNode**: This class represents a node within a search tree. It holds a board state and the current depth in the search. This structure is fundamental for implementing algorithms such as minimax, as it enables the exploration of future game states in a structured manner.

---

**Core Board Implementation (Base Folder)**

The *Board* class is central to the chess logic. It utilises an 8×8 matrix (implemented as a two-dimensional array) to represent the chessboard. Key features include:

- **Indexers and Constructors**: The class provides indexers that allow access to pieces using either a custom *Square* structure or explicit file and rank values. It offers both a constructor that initialises the board with specified square–piece pairs and a deep copy constructor to create an independent duplicate of an existing board.

- **Starting Position**: A static array, *StartingPositionPieces*, defines the standard initial placement of pieces for both sides. This facilitates the setup of a new game.

- **Move Execution and Special Moves**: The *MovePiece* method is responsible for updating the board when a move is executed. It also takes into account special moves (via inheritance from a special move class) by invoking any associated behaviour, such as castling or en passant.

- **Textual Representation**: The *ToTextArt* method generates a textual visualisation of the board, which may be used for debugging or logging purposes.

---

**Rules and Move Legality**

The static *Rules* class provides methods to verify the legality of moves and board positions:

- **Checkmate, Stalemate, and Check**: Methods such as *IsPlayerCheckmated*, *IsPlayerStalemated*, and *IsPlayerInCheck* assess the game state based on the number of legal moves available and whether the king is under threat.

- **Move Validation**: The method *MoveObeysRules* verifies that a proposed move does not violate the game rules. This is accomplished by simulating the move on a duplicate board and ensuring that the moving side does not end up in check.

- **Square Attack Determination**: The *IsSquareAttacked* method iterates over potential attack vectors. By considering various offsets, it evaluates whether a given square is threatened by an opposing piece, accounting for the distinct movement patterns of queens, bishops, rooks, kings, pawns, and knights.

---

**The Square Structure**

The *Square* structure provides a representation of a single board square. Its main attributes and functionalities include:

- **File and Rank Storage**: Each square is identified by its file (column) and rank (row), and the structure includes methods to check whether these values are within the valid range (1 to 8).

- **Operator Overloading**: Operators have been overloaded to allow for the intuitive addition of square positions and for comparing squares, which simplifies many board-related calculations.

- **String Representation**: A custom *ToString* method ensures that each square can be easily converted into a standard chess notation format.

---

**Timeline Utility**

The generic *Timeline&lt;T&gt;* class manages a sequential history of game states or moves (for example, half-moves). Its features include:

- **Current State Management**: The class maintains a head index that represents the current state within the timeline. This facilitates operations such as undoing moves or branching out in the case of alternate move histories.

- **Future Elements Pruning**: When a new move is added that deviates from the existing timeline, future elements are pruned, ensuring that the timeline remains consistent with the actual game progress.

---

**Game Serialization**

Within the *GameSerialization* folder, the *FENSerializer* class is implemented. Its responsibilities include:

- **Serialisation to FEN**: The *Serialize* method converts the current game state into a FEN (Forsyth–Edwards Notation) string. This involves constructing a board string that accurately represents the positions of all pieces, as well as appending information on the side to move, castling rights, en passant possibilities, the half-move clock, and the turn number.

- **Deserialization from FEN**: Conversely, the *Deserialize* method reconstructs a game from a FEN string. It parses the board representation, recreates the corresponding square–piece pairs, and restores the game conditions.

- **Helper Methods**: Methods such as *CalculateBoardString*, *GetFENPieceSymbol*, and *CalculateCastlingInfoString* assist in ensuring that the FEN representation adheres to the standard format, thereby enabling reliable game saving and loading.

---

**Game Conditions**

The *GameConditions* structure encapsulates non-board aspects of the game state, such as:

- **Castling Rights and En Passant**: It records whether each side retains the ability to castle on either side, as well as the current en passant square (if any).

- **Move Counters**: The half-move clock and turn number are maintained, which are essential for determining draw conditions and the progression of the game.

- **Updating Game Conditions**: The *CalculateEndingConditions* method computes new conditions after a half-move has been executed. It considers whether key pieces (such as kings or rooks) have moved, thereby updating castling rights, and adjusts the en passant square and half-move clock accordingly.

---

**Game Pieces**

The abstract *Piece* class defines the common properties and methods shared by all chess pieces. It stores the owner (or side) of the piece and mandates the implementation of the *CalculateLegalMoves* method. Moreover, it provides a *ToTextArt* method that utilises Unicode symbols to represent each piece in a human-readable format. The generic subclass *Piece<T>* simplifies the process of deep copying by returning a new instance of the specific piece type with the same owner.

---

**Individual Piece Implementations**

- **Bishop**  
  The *Bishop* class generates legal moves by iterating over a set of diagonal offsets. For each offset, it continuously evaluates squares in that direction until either an illegal move is detected or the path is blocked by another piece. Moves are only added if they pass the legality check performed by the rules engine.

- **King**  
  The *King* class first evaluates all adjacent squares through a helper method that considers the surrounding offsets. Additionally, it handles the special castling move. The castling logic is implemented by checking that the king is not in check and that the castling rights are still available. It further ensures that the squares between the king and the rook are both unoccupied and not under attack. This careful examination reflects the complexity of castling rules in standard chess.

- **Knight**  
  The *Knight* class utilises a fixed set of knight-specific offsets. It iterates through these predefined moves and adds each legal move to the result if the move does not contravene any game rules. This approach is relatively straightforward given the unique L-shaped movement pattern of the knight.

- **Pawn**  
  The *Pawn* class is more intricate due to the multiple movement rules it must observe. It divides its move calculations into three distinct areas:
    - *Forward Movement*: The pawn first checks the square directly ahead. If unoccupied, a normal move is allowed. If the pawn is in its initial position, the possibility of a two-square move is also examined.
    - *Diagonal Attacks*: The pawn considers diagonal squares for capturing enemy pieces. When such a move results in the pawn reaching the far rank, a promotion move is generated.
    - *En Passant*: The class checks whether an en passant capture is applicable by verifying that the pawn is on the correct rank and that the target square meets the specific conditions for en passant.

- **Queen**  
  The *Queen* class effectively combines the movement capabilities of both the rook and the bishop. It iterates over all surrounding offsets—encompassing both cardinal and diagonal directions—and continues in each direction until an obstruction is encountered. This comprehensive approach enables the queen to cover the entire board, subject to standard chess rules.

- **Rook**  
  The *Rook* class focuses on moves along the cardinal directions. By iterating over a set of cardinal offsets, it examines each square along a given direction until it reaches the board’s edge or an occupied square. The move is only included if it complies with the rules regarding piece movement and capture.