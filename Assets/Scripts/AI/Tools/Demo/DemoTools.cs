using System;
using System.ComponentModel;
using System.Threading;
using UnityEngine;

namespace AI.Tools.Demo
{
    /// <summary>
    /// Test tools: an instant one (get_time) and one that takes game time and can be
    /// cancelled (wait_seconds).
    /// </summary>
    public class DemoTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;

        private void OnEnable() => toolSet.RegisterFrom(this);
        private void OnDisable() => toolSet.UnregisterFrom(this);

        [AgentTool, Description("Get the current real-world time and how long the game has been running.")]
        private string GetTime() =>
            $"Real time: {DateTime.Now:HH:mm:ss}. Game running for {Time.time:F1} s.";

        [AgentTool, Description("Wait for a number of seconds of game time before continuing.")]
        private async Awaitable<ToolResult> WaitSeconds(
            [Description("How long to wait, between 0 and 30.")] float seconds,
            CancellationToken ct)
        {
            if (seconds < 0 || seconds > 30)
                return ToolResult.Fail("seconds must be between 0 and 30.");

            await Awaitable.WaitForSecondsAsync(seconds, ct);
            return ToolResult.Ok($"Waited {seconds:F1} s.");
        }
    }
}
