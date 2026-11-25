using System.ComponentModel;
using System.Linq;
using AI.Tools;
using UnityEngine;

namespace Demo.ChessDemo
{
    /// <summary>The planner sub-agent's tool: plays hypothetical moves on a copy of the board, never the real game.</summary>
    public class ChessPlannerTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;

        private void OnEnable() => toolSet.RegisterFrom(this);
        private void OnDisable() => toolSet.UnregisterFrom(this);

        [AgentTool, Description("Play a sequence of moves on a copy of the current board, alternating sides and starting " +
                                "with whoever is to move, and see the result: the board, the material and the legal moves " +
                                "of the side to move next. Empty = the current position. The real game doesn't change.")]
        private ToolResult Simulate(
            [Description("Moves in order, separated by commas, e.g. \"e7e5, g1f3\". Empty for the current position.")] string moves)
        {
            var game = ChessGame.Current;
            if (game == null) return ToolResult.Fail("There is no game.");

            var board = game.Board;
            var played = (moves ?? "").Split(',').Select(m => m.Trim()).Where(m => m.Length > 0).ToList();
            for (var i = 0; i < played.Count; i++)
            {
                var move = board.FindMove(played[i]);
                if (move == null)
                    return ToolResult.Fail($"Move {i + 1} ('{played[i]}') isn't legal for {board.Turn} there. " +
                                           $"{board.Turn}'s legal moves:\n{board.DescribeMoves()}");
                board = board.Apply(move.Value);
            }

            var after = played.Count == 0 ? "Current position" : $"After {string.Join(", ", played)}";
            var result = board.Result();
            return ToolResult.Ok($"{after}:\n{board.ToText()}\n{board.DescribeMaterial()}\n" +
                                 (result ?? (board.InCheck(board.Turn) ? $"{board.Turn} is in check. " : "") +
                                     $"{board.Turn} to move. Legal moves:\n{board.DescribeMoves()}"));
        }
    }
}
