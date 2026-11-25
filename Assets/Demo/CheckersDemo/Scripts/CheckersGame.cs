using System.Collections.Generic;
using System.Linq;
using AI;
using Demo.Shared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Demo.CheckersDemo
{
    /// <summary>
    /// A game of checkers: the player (Black) clicks pieces, the AI (White) is an <see cref="Agent"/>
    /// that the game asks to move whenever it's White's turn. Owns the board, draws the pieces
    /// (square (f, r) is centred at (f - 3.5, 0, r - 3.5)) and shows the status in the top-left corner.
    /// </summary>
    public class CheckersGame : MonoBehaviour
    {
        private const string Sender = "Checkers";

        [SerializeField, Tooltip("The AI player. Leave empty to use the Agent next to the CheckersPlayerTools.")]
        private Agent ai;

        [SerializeField] private Material blackMaterial;
        [SerializeField] private Material whiteMaterial;
        [SerializeField] private Material highlightMaterial;
        [SerializeField, Min(1)] private int aiAttemptsPerTurn = 3;

        private readonly List<GameObject> _views = new();
        private Vector2Int? _selected;
        private int _aiAttempts;
        private float _nextAiAt = float.MaxValue;
        private Label _status;

        public static CheckersGame Current { get; private set; }

        public Board Board { get; private set; }
        public Side Turn { get; private set; }
        public Side? Winner { get; private set; }
        public Move LastMove { get; private set; }
        public bool IsOver => Winner != null;

        public void NewGame()
        {
            if (ai != null) ai.Cancel();
            Board = Board.Start();
            Turn = Side.Black;
            Winner = null;
            LastMove = null;
            _selected = null;
            Redraw();
        }

        /// <summary>The position as the AI sees it on its turn.</summary>
        public string Describe(Side side)
        {
            var moves = Board.LegalMoves(side);
            return $"{Board.ToText()}\n{Board.DescribeMaterial()}\n" +
                   (LastMove != null ? $"Last move: {LastMove}.\n" : "") +
                   $"{side}'s legal moves: {string.Join(", ", moves)}" +
                   (moves.Any(m => m.Captured.Length > 0) ? " (captures are mandatory)." : ".");
        }

        /// <summary>Plays <paramref name="notation"/> for White. Null when done, otherwise why not.</summary>
        public string TryAiMove(string notation)
        {
            if (IsOver) return "The game is over.";
            if (Turn != Side.White) return "It's not your turn.";

            var move = Board.FindMove(Side.White, notation);
            if (move == null) return $"'{notation}' isn't a legal move. Legal moves: {string.Join(", ", Board.LegalMoves(Side.White))}.";

            Play(move);
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
                var tools = FindAnyObjectByType<CheckersPlayerTools>();
                if (tools != null) ai = tools.GetComponent<Agent>();
            }

            if (ai == null) Debug.LogError("[CheckersGame] No AI Agent found.", this);
            else ai.StateChanged += OnAiStateChanged;

            if (WorldOverlay.Current != null) BuildHud(WorldOverlay.Current.Root);
            NewGame();
        }

        private void Update()
        {
            UpdateStatus();
            if (IsOver) return;
            if (Turn == Side.Black) HandleClick();
            else if (ai != null && !ai.IsBusy && Time.unscaledTime >= _nextAiAt) AskAi();
        }

        // ---- Player ----

        private void HandleClick()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Camera.main == null) return;
            if (!Physics.Raycast(Camera.main.ScreenPointToRay(mouse.position.ReadValue()), out var hit)) return;

            var square = new Vector2Int(Mathf.FloorToInt(hit.point.x + 4f), Mathf.FloorToInt(hit.point.z + 4f));
            if (!Board.InBounds(square)) return;

            var moves = Board.LegalMoves(Side.Black);
            var move = _selected == null ? null : moves.FirstOrDefault(m => m.From == _selected && m.To == square);
            if (move != null)
            {
                Play(move);
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
                // Keep the game going when the model can't produce a move.
                var moves = Board.LegalMoves(Side.White);
                Debug.LogWarning("[CheckersGame] The AI didn't move; playing a random legal move for it.", this);
                Play(moves[Random.Range(0, moves.Count)]);
                return;
            }

            _aiAttempts++;
            _nextAiAt = float.MaxValue; // set again when the run ends (OnAiStateChanged)
            ai.ResetConversation();

            var prompt =
                "We're playing checkers. You are White (w = man, W = king) and move down the board; I'm Black (b, B). " +
                (_aiAttempts > 1 ? "You haven't moved yet. " : "") + "It's your turn:\n" + Describe(Side.White) + "\n" +
                "When there's a real choice, use consult_planner to look a few moves ahead before you decide; " +
                "with only one legal move, just play it. Then call make_move. After moving, reply to me with one short sentence.";
            _ = ai.Send(prompt, Sender);
        }

        private void OnAiStateChanged(AgentState state)
        {
            if (state == AgentState.Idle) _nextAiAt = Time.unscaledTime + 1f; // retries if it's still White's turn
        }

        // ---- Game ----

        private void Play(Move move)
        {
            Board = Board.Apply(move);
            LastMove = move;
            _selected = null;
            Turn = Board.Other(Turn);
            if (Board.LegalMoves(Turn).Count == 0) Winner = Board.Other(Turn); // no moves left = lost

            if (!IsOver && Turn == Side.White)
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

            for (var rank = 0; rank < 8; rank++)
            for (var file = 0; file < 8; file++)
            {
                var square = new Vector2Int(file, rank);
                var piece = Board[square];
                if (piece == Piece.Empty) continue;
                var material = Board.Owner(piece) == Side.Black ? blackMaterial : whiteMaterial;
                AddDisc(square, material, Board.IsKing(piece) ? 0.3f : 0.12f, 0.8f);
            }

            var marks = new List<Vector2Int>();
            if (_selected is { } selected)
                marks.AddRange(Board.LegalMoves(Side.Black).Where(m => m.From == selected).Select(m => m.To).Append(selected));
            else if (LastMove != null)
                marks.AddRange(LastMove.Path);
            foreach (var square in marks) AddDisc(square, highlightMaterial, 0.02f, 0.95f);
        }

        private void AddDisc(Vector2Int square, Material material, float height, float width)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>()); // clicks hit the board underneath
            disc.transform.SetParent(transform, false);
            disc.transform.position = new Vector3(square.x - 3.5f, height / 2f, square.y - 3.5f);
            disc.transform.localScale = new Vector3(width, height / 2f, width); // a cylinder is 2 units tall
            disc.GetComponent<Renderer>().sharedMaterial = material;
            _views.Add(disc);
        }

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
            _status.text = IsOver ? (Winner == Side.Black ? "You win!" : "White wins.")
                : Turn == Side.Black ? (_selected == null ? "Your move (Black): click a piece." : "Click where to move it.")
                : "White (AI) is thinking...";
        }
    }
}
