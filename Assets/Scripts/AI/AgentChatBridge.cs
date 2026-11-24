using System;
using AI.Contract;
using AI.Tools;
using Newtonsoft.Json;
using UI;
using UnityEngine;

namespace AI
{
    /// <summary>
    /// Wires an <see cref="Agent"/> to a <see cref="ChatBox"/>; neither knows about the other.
    /// Chat commands: "/stop" cancels the current run, "/reset" clears the conversation.
    /// </summary>
    public class AgentChatBridge : MonoBehaviour
    {
        [SerializeField] private Agent agent;
        [SerializeField] private ChatBox chatBox;
        [SerializeField] private bool showThoughts = true;
        [SerializeField] private bool showToolCalls = true;

        private void OnEnable()
        {
            if (agent == null || chatBox == null)
            {
                Debug.LogError("[AgentChatBridge] Assign both the Agent and the ChatBox.", this);
                enabled = false;
                return;
            }

            chatBox.MessageSubmitted += OnSubmitted;
            agent.ThoughtReceived += OnThought;
            agent.ActionStarted += OnAction;
            agent.ObservationReceived += OnObservation;
            agent.FinalAnswer += OnFinal;
            agent.Failed += OnFailed;
            agent.Cancelled += OnCancelled;
            agent.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (agent == null || chatBox == null) return;

            chatBox.MessageSubmitted -= OnSubmitted;
            agent.ThoughtReceived -= OnThought;
            agent.ActionStarted -= OnAction;
            agent.ObservationReceived -= OnObservation;
            agent.FinalAnswer -= OnFinal;
            agent.Failed -= OnFailed;
            agent.Cancelled -= OnCancelled;
            agent.StateChanged -= OnStateChanged;
        }

        private async void Start()
        {
            try
            {
                if (!Connector.HasInstance) return; // Agent reports this on first send
                var url = Connector.Instance.BaseUrl;
                var healthy = await Connector.Instance.IsHealthy(destroyCancellationToken);
                Notice(healthy ? $"Connected to {url}." : $"Agent server not reachable at {url}.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnSubmitted(string text)
        {
            switch (text)
            {
                case "/stop":
                    agent.Cancel();
                    return;
                case "/reset":
                    if (agent.ResetConversation())
                    {
                        chatBox.Clear();
                        Notice("Conversation cleared.");
                    }
                    else Notice("Can't reset while the agent is working. Use /stop first.");
                    return;
            }

            if (agent.IsBusy)
            {
                Notice("The agent is still working. Wait, or type /stop.");
                return;
            }

            chatBox.AddMessage(text, "You", "chat-message--user");
            _ = agent.Send(text);
        }

        private void OnThought(string thought)
        {
            if (showThoughts) chatBox.AddMessage(thought, "Thought", "chat-message--thought");
        }

        private void OnAction(ActionCall action)
        {
            if (showToolCalls)
                chatBox.AddMessage($"{action.Tool} {action.Args?.ToString(Formatting.None) ?? "{}"}",
                    "Action", "chat-message--action");
        }

        private void OnObservation(ActionCall action, ToolResult result)
        {
            if (showToolCalls)
                chatBox.AddMessage(result.Content, result.IsError ? "Observation (failed)" : "Observation",
                    result.IsError ? "chat-message--error" : "chat-message--observation");
        }

        private void OnFinal(string answer) => chatBox.AddMessage(answer, "Agent", "chat-message--agent");
        private void OnFailed(string error) => chatBox.AddMessage(error, "Error", "chat-message--error");
        private void OnCancelled() => Notice("Stopped.");

        private void OnStateChanged(AgentState state) => chatBox.SetStatus(state switch
        {
            AgentState.Thinking => "Thinking...",
            AgentState.RunningTool => "Running tool...",
            _ => null,
        });

        private void Notice(string text) => chatBox.AddMessage(text, null, "chat-message--system");
    }
}
