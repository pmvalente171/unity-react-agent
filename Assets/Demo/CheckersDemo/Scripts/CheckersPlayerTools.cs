using System.ComponentModel;
using AI;
using AI.Tools;
using UnityEngine;

namespace Demo.CheckersDemo
{
    /// <summary>
    /// The AI player's tools: read the board, move, and consult the planner sub-agent
    /// (a <see cref="SubAgentTool"/> wrapping the planner's <see cref="Agent"/>).
    /// </summary>
    public class CheckersPlayerTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;
        [SerializeField, Tooltip("The planner sub-agent (it has its own ToolSet with CheckersPlannerTools).")]
        private Agent planner;

        [SerializeField, TextArea(3, 8)] private string plannerInstructions =
            "You are the planning assistant of the White checkers player. Use simulate to try the candidate moves " +
            "and Black's strongest replies, two or three moves deep. Prefer lines that win pieces, keep yours safe " +
            "and crown kings. Answer with the move you recommend and why, in two or three sentences.";

        private SubAgentTool _consultPlanner;

        private void OnEnable()
        {
            toolSet.RegisterFrom(this);
            if (planner == null) return;
            _consultPlanner ??= new SubAgentTool("consult_planner",
                "Ask your planner to look ahead: it simulates moves and replies and recommends a move. " +
                "Say which moves you're considering and what worries you.", planner, plannerInstructions, sender: "White");
            toolSet.Register(_consultPlanner);
        }

        private void OnDisable()
        {
            toolSet.UnregisterFrom(this);
            toolSet.Unregister(_consultPlanner);
        }

        [AgentTool, Description("Show the board, the material count and your legal moves.")]
        private string GetBoard() =>
            CheckersGame.Current == null ? "There is no game." : CheckersGame.Current.Describe(Side.White);

        [AgentTool, Description("Play one of your legal moves, e.g. \"c5-d4\" or \"e5xc3xa1\". This ends your turn.")]
        private ToolResult MakeMove([Description("The move, exactly as listed in your legal moves.")] string move)
        {
            if (CheckersGame.Current == null) return ToolResult.Fail("There is no game.");
            var error = CheckersGame.Current.TryAiMove(move);
            return error == null ? ToolResult.Ok($"You played {move}. Now it's Black's turn.") : ToolResult.Fail(error);
        }
    }
}
