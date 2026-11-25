using System.Collections.Generic;
using System.Linq;
using AI;
using Demo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Demo.ChessDemo
{
    /// <summary>
    /// A game of chess: the player (White) clicks pieces, the AI (Black) is an <see cref="Agent"/> the game
    /// asks to move on Black's turn. Draws the pieces from the meshes of <c>piecesModel</c> (children named
    /// Pawn, Knight, ...); square i is centred at (i % 8 - 3.5, 0, i / 8 - 3.5).
    /// </summary>
    public class ChessGame : MonoBehaviour
    {
        private const string Sender = "Chess";

        [SerializeField, Tooltip("The AI player. Leave empty to use the Agent next to the ChessPlayerTools.")]
        private Agent ai;

        [SerializeField, Tooltip("Model whose children are named Pawn, Knight, Bishop, Rook, Queen and King.")]
        private GameObject piecesModel;

        [SerializeField, Min(0.01f)] private float pieceScale = 0.4f;
        [SerializeField] private Material whiteMaterial;
        [SerializeField] private Material blackMaterial;
        [SerializeField] private Material highlightMaterial;
        [SerializeField, Min(1)] private int aiAttemptsPerTurn = 3;

        private readonly List<GameObject> _views = new();
        private int? _selected;
        private Move? _lastMove;
        private int _aiAttempts;
        private float _nextAiAt = float.MaxValue;
        private Label _status;

        public static ChessGame Current { get; private set; }

        public ChessBoard Board { get; private set; }
        public string Result { get; private set; }
        public bool IsOver => Result != null;

        public void NewGame()
        {
            if (ai != null) ai.Cancel();
            Board = ChessBoard.Start();
            Result = null;
            _lastMove = null;
            _selected = null;
            Redraw();
        }

        /// <summary>The position as the side to move sees it.</summary>
        public string Describe() =>
            $"{Board.ToText()}\n{Board.DescribeMaterial()}\n" +
            (_lastMove != null ? $"Last move: {_lastMove}.\n" : "") +
            (Board.InCheck(Board.Turn) ? $"{Board.Turn} is in check!\n" : "") +
            $"{Board.Turn}'s legal moves:\n{Board.DescribeMoves()}";

        /// <summary>Plays <paramref name="notation"/> for Black. Null when done, otherwise why not.</summary>
        public string TryAiMove(string notation)
        {
            if (IsOver) return "The game is over.";
            if (Board.Turn != Side.Black) return "It's not your turn.";

            var move = Board.FindMove(notation);
            if (move == null) return $"'{notation}' isn't a legal move. Legal moves:\n{Board.DescribeMoves()}";

            Play(move.Value);
            return null;
        }

        private void OnEnable() => Current = this;

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (ai != null) ai.StateChanged -= OnAiStateChanged;
        }

        private void Start()
        {
            if (ai == null)
            {
                var tools = FindAnyObjectByType<ChessPlayerTools>();
                if (tools != null) ai = tools.GetComponent<Agent>();
            }

            if (ai == null) Debug.LogError("[ChessGame] No AI Agent found.", this);
            else ai.StateChanged += OnAiStateChanged;

            if (WorldOverlay.Current != null) BuildHud(WorldOverlay.Current.Root);
            NewGame();
        }

        private void Update()
        {
            UpdateStatus();
            if (IsOver) return;
            if (Board.Turn == Side.White) HandleClick();
            else if (ai != null && !ai.IsBusy && Time.unscaledTime >= _nextAiAt) AskAi();
        }

        // ---- Player ----

        private void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Camera.main == null) return;
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(mouse.position.ReadValue()), out var hit)) return;

            int f = Mathf.FloorToInt(hit.point.x + 4f), r = Mathf.FloorToInt(hit.point.z + 4f);
            if (f is < 0 or > 7 || r is < 0 or > 7) return;
            var square = r * 8 + f;

            var moves = Board.LegalMoves();
            // Promotions always pick a queen: it's listed first.
            var move = moves.Where(m => m.From == _selected && m.To == square).Cast<Move?>().FirstOrDefault();
            if (move != null)
            {
                Play(move.Value);
                return;
            }

            _selected = moves.Any(m => m.From == square) ? square : null;
            Redraw();
        }

        // ---- AI ----

        private void AskAi()
        {
            if (_aiAttempts >= aiAttemptsPerTurn)
            {
                var moves = Board.LegalMoves(); // keep the game going when the model can't produce a move
                Debug.LogWarning("[ChessGame] The AI didn't move; playing a random legal move for it.", this);
                Play(moves[Random.Range(0, moves.Count)]);
                return;
            }

            _aiAttempts++;
            _nextAiAt = float.MaxValue; // set again when the run ends (OnAiStateChanged)
            ai.ResetConversation();

            var prompt =
                "We're playing chess. You are Black (lowercase letters) and I'm White (uppercase). Moves are written " +
                "from-square + to-square, e.g. e7e5; promotions add the piece, e.g. a2a1q. " +
                (_aiAttempts > 1 ? "You haven't moved yet. " : "") + "It's your turn:\n" + Describe() + "\n" +
                "Use consult_planner to look a few moves ahead before you decide, unless the move is obvious " +
                "(only one legal move, or an obvious recapture). Then call make_move. After moving, reply to me with one short sentence.";
            _ = ai.Send(prompt, Sender);
        }

        private void OnAiStateChanged(AgentState state)
        {
            if (state == AgentState.Idle) _nextAiAt = Time.unscaledTime + 1f; // retries if it's still Black's turn
        }

        // ---- Game ----

        private void Play(Move move)
        {
            Board = Board.Apply(move);
            _lastMove = move;
            _selected = null;
            Result = Board.Result();

            if (!IsOver && Board.Turn == Side.Black)
            {
                _aiAttempts = 0;
                _nextAiAt = Time.unscaledTime + 0.5f;
            }

            Redraw();
        }

        private void Redraw()
        {
            foreach (var view in _views) Destroy(view);
            _views.Clear();

            for (var square = 0; square < 64; square++)
                if (Board[square] != '.') AddPiece(square, Board[square]);

            var marks = new List<int>();
            if (_selected is { } selected)
                marks.AddRange(Board.LegalMoves().Where(m => m.From == selected).Select(m => m.To).Append(selected));
            else if (_lastMove is { } last)
                marks.AddRange(new[] { last.From, last.To });
            foreach (var square in marks.Distinct()) AddMarker(square);
        }

        private void AddPiece(int square, char piece)
        {
            var template = piecesModel != null ? piecesModel.transform.Find(Capitalized(ChessBoard.PieceName(piece))) : null;
            if (template == null || !template.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null)
            {
                Debug.LogError($"[ChessGame] No mesh for {ChessBoard.PieceName(piece)} in the pieces model.", this);
                return;
            }

            var white = ChessBoard.Owner(piece) == Side.White;
            var view = new GameObject(ChessBoard.PieceName(piece));
            view.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = view.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = white ? whiteMaterial : blackMaterial;

            // Keep the model's import rotation and scale; Black's pieces face the other way.
            view.transform.SetParent(transform, false);
            view.transform.rotation = Quaternion.Euler(0f, white ? 0f : 180f, 0f) * template.rotation;
            view.transform.localScale = template.lossyScale * pieceScale;

            // Stand it on the centre of its square, whatever the mesh's pivot.
            var bounds = renderer.bounds;
            view.transform.position += new Vector3(square % 8 - 3.5f - bounds.center.x, -bounds.min.y, square / 8 - 3.5f - bounds.center.z);
            _views.Add(view);
        }

        private void AddMarker(int square)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(marker.GetComponent<Collider>()); // clicks hit the board underneath
            marker.transform.SetParent(transform, false);
            marker.transform.position = new Vector3(square % 8 - 3.5f, 0.01f, square / 8 - 3.5f);
            marker.transform.localScale = new Vector3(0.9f, 0.01f, 0.9f);
            marker.GetComponent<Renderer>().sharedMaterial = highlightMaterial;
            _views.Add(marker);
        }

        private static string Capitalized(string name) => char.ToUpper(name[0]) + name.Substring(1);

        // ---- HUD ----

        private void BuildHud(VisualElement root)
        {
            var panel = new VisualElement();
            var s = panel.style;
            s.position = Position.Absolute;
            s.left = s.top = 24;
            s.paddingLeft = s.paddingRight = 14;
            s.paddingTop = s.paddingBottom = 10;
            s.backgroundColor = new Color(0.07f, 0.07f, 0.1f, 0.88f);
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 10;

            _status = new Label { pickingMode = PickingMode.Ignore };
            _status.style.color = Color.white;
            _status.style.fontSize = 18;
            panel.Add(_status);
            panel.Add(new Button(NewGame) { text = "New game" });
            root.Add(panel);
        }

        private void UpdateStatus()
        {
            if (_status == null) return;
            var check = !IsOver && Board.InCheck(Board.Turn) ? " Check!" : "";
            _status.text = IsOver ? Result
                : Board.Turn == Side.White ? (_selected == null ? "Your move (White): click a piece." : "Click where to move it.") + check
                : "Black (AI) is thinking..." + check;
        }
    }
}
