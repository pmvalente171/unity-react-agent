using System.ComponentModel;
using System.Linq;
using AI.Tools;
using UnityEngine;

namespace Demo.CheckersDemo
{
    /// <summary>
    /// The planner sub-agent's tool: plays hypothetical moves on a copy of the board, never the real game.
    /// </summary>
    public class CheckersPlannerTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;

        private void OnEnable() => toolSet.RegisterFrom(this);
        private void OnDisable() => toolSet.UnregisterFrom(this);

        [AgentTool, Description("Play a sequence of moves on a copy of the current board, alternating sides and starting " +
                                "with whoever is to move, and see the result: the board, the material and the legal " +
                                "moves of the side to move next. Empty = the current position. The real game doesn't change.")]
        private ToolResult Simulate(
            [Description("Moves in order, separated by commas, e.g. \"c5-d4, e3xc5\". Empty for the current position.")] string moves)
        {
            var game = CheckersGame.Current;
            if (game == null) return ToolResult.Fail("There is no game.");

            var board = game.Board;
            var side = game.Turn;
            var played = (moves ?? "").Split(',').Select(m => m.Trim()).Where(m => m.Length > 0).ToList();
            for (var i = 0; i < played.Count; i++)
            {
                var move = board.FindMove(side, played[i]);
                if (move == null)
                    return ToolResult.Fail($"Move {i + 1} ('{played[i]}') isn't legal for {side} there. " +
                                           $"{side}'s legal moves: {string.Join(", ", board.LegalMoves(side))}.");
                board = board.Apply(move);
                side = Board.Other(side);
            }

            var next = board.LegalMoves(side);
            var after = played.Count == 0 ? "Current position" : $"After {string.Join(", ", played)}";
            return ToolResult.Ok($"{after}:\n{board.ToText()}\n{board.DescribeMaterial()}\n" +
                                 (next.Count == 0
                                     ? $"{side} has no moves: {Board.Other(side)} wins."
                                     : $"{side} to move. Legal moves: {string.Join(", ", next)}."));
        }
    }
}
