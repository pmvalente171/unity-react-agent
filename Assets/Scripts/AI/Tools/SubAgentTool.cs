using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Tools
{
    /// <summary>
    /// Turns another <see cref="Agent"/> into a tool: the caller sends it a task, the sub-agent runs its
    /// own ReAct loop with its own <see cref="ToolSet"/>, and its final answer becomes the observation.
    /// The sub-agent's steps go through its own events, so a chat bridge on it shows them too.
    /// <code>
    /// _planner = new SubAgentTool("consult_planner", "Ask the planner to look ahead.", plannerAgent, instructions);
    /// void OnEnable()  =&gt; toolSet.Register(_planner);
    /// void OnDisable() =&gt; toolSet.Unregister(_planner);
    /// </code>
    /// </summary>
    public sealed class SubAgentTool : IAgentTool
    {
        private readonly Agent _agent;
        private readonly string _instructions;
        private readonly string _sender;

        public string Name { get; }
        public string Description { get; }
        public JObject Parameters { get; } = ToolSchema.Object()
            .Property("task", "string", "What you want from it, with everything it needs to know.")
            .Build();

        /// <param name="instructions">Put before every task: the sub-agent's role (the system prompt is server-side).</param>
        /// <param name="sender">Who the task is shown as coming from (chat label).</param>
        /// <param name="freshEachCall">Forget earlier tasks, so every call starts from an empty conversation.</param>
        public SubAgentTool(string name, string description, Agent agent, string instructions = null,
            string sender = "Agent", bool freshEachCall = true)
        {
            Name = name;
            Description = description;
            _agent = agent;
            _instructions = instructions;
            _sender = sender;
            FreshEachCall = freshEachCall;
        }

        public bool FreshEachCall { get; set; }

        public async Awaitable<ToolResult> Execute(JObject args, CancellationToken ct)
        {
            var task = args?.Value<string>("task");
            if (string.IsNullOrWhiteSpace(task)) return ToolResult.Fail("The task is empty.");
            if (_agent == null) return ToolResult.Fail($"{Name} has no agent.");
            if (_agent.IsBusy) return ToolResult.Fail($"{Name} is still busy with another task.");

            string answer = null, error = null;
            void OnFinal(string text) => answer = text;
            void OnFailed(string text) => error = text;

            _agent.FinalAnswer += OnFinal;
            _agent.Failed += OnFailed;
            try
            {
                if (FreshEachCall) _agent.ResetConversation();
                using (ct.Register(_agent.Cancel)) // stopping the caller stops the sub-agent
                    await _agent.Send(string.IsNullOrEmpty(_instructions) ? task : $"{_instructions}\n\nTask: {task}", _sender);
            }
            finally
            {
                _agent.FinalAnswer -= OnFinal;
                _agent.Failed -= OnFailed;
            }

            ct.ThrowIfCancellationRequested();
            return answer != null ? ToolResult.Ok(answer) : ToolResult.Fail($"{Name} gave no answer: {error ?? "it was stopped"}.");
        }
    }
}
