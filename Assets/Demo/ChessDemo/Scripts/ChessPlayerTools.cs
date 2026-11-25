using System.ComponentModel;
using AI;
using AI.Tools;
using UnityEngine;

namespace Demo.ChessDemo
{
    /// <summary>
    /// The AI player's tools: read the board, move, and consult the planner sub-agent
    /// (a <see cref="SubAgentTool"/> wrapping the planner's <see cref="Agent"/>).
    /// </summary>
    public class ChessPlayerTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;
        [SerializeField, Tooltip("The planner sub-agent (it has its own ToolSet with ChessPlannerTools).")]
        private Agent planner;

        [SerializeField, TextArea(3, 8)] private string plannerInstructions =
            "You are the planning assistant of the Black chess player. Use simulate to try the candidate moves and " +
            "White's strongest replies, two or three moves deep. Look for hanging pieces, captures, checks and threats. " +
            "Answer with the move you recommend and why, in two or three sentences.";

        private SubAgentTool _consultPlanner;

        private void OnEnable()
        {
            toolSet.RegisterFrom(this);
            if (planner == null) return;
            _consultPlanner ??= new SubAgentTool("consult_planner",
                "Ask your planner to look ahead: it simulates moves and replies and recommends a move. " +
                "Say which moves you're considering and what worries you.", planner, plannerInstructions, sender: "Black");
            toolSet.Register(_consultPlanner);
        }

        private void OnDisable()
        {
            toolSet.UnregisterFrom(this);
            toolSet.Unregister(_consultPlanner);
        }

        [AgentTool, Description("Show the board, the material and your legal moves.")]
        private string GetBoard() => ChessGame.Current == null ? "There is no game." : ChessGame.Current.Describe();

        [AgentTool, Description("Play one of your legal moves, e.g. \"e7e5\" (from-square + to-square). This ends your turn.")]
        private ToolResult MakeMove([Description("The move, as listed in your legal moves.")] string move)
        {
            if (ChessGame.Current == null) return ToolResult.Fail("There is no game.");
            var error = ChessGame.Current.TryAiMove(move);
            return error == null ? ToolResult.Ok($"You played {move}. Now it's White's turn.") : ToolResult.Fail(error);
        }
    }
}
