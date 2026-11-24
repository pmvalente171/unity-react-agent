using System;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace AI.Tools.Demo
{
    /// <summary>
    /// Two test tools on one component: an instant one (get_time) and one that takes
    /// game time and can be cancelled (wait_seconds).
    /// </summary>
    public class DemoTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;

        private IAgentTool[] _tools;

        private sealed class WaitArgs
        {
            [JsonProperty("seconds")] public float Seconds;
        }

        private void Awake()
        {
            _tools = new IAgentTool[]
            {
                new DelegateTool<NoArgs>("get_time",
                    "Get the current real-world time and how long the game has been running.",
                    null, GetTime),

                new DelegateTool<WaitArgs>("wait_seconds",
                    "Wait for a number of seconds of game time before continuing.",
                    ToolSchema.Object()
                        .Property("seconds", "number", "How long to wait, between 0 and 30.")
                        .Build(),
                    WaitSeconds),
            };
        }

        private void OnEnable() => toolSet.RegisterAll(_tools);
        private void OnDisable() => toolSet.UnregisterAll(_tools);

        private static ToolResult GetTime(NoArgs _) =>
            ToolResult.Ok($"Real time: {DateTime.Now:HH:mm:ss}. Game running for {Time.time:F1} s.");

        private static async Awaitable<ToolResult> WaitSeconds(WaitArgs args, CancellationToken ct)
        {
            if (args.Seconds < 0 || args.Seconds > 30)
                return ToolResult.Fail("seconds must be between 0 and 30.");

            await Awaitable.WaitForSecondsAsync(args.Seconds, ct);
            return ToolResult.Ok($"Waited {args.Seconds:F1} s.");
        }
    }
}
