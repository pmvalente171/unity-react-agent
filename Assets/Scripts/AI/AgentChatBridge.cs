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
    /// Put it next to the ChatBox. Empty fields are filled automatically: the ChatBox on this
    /// GameObject, and the only Agent in the scene (assign it when there are several).
    /// Chat commands: "/stop" cancels the current run, "/reset" clears the conversation.
    /// </summary>
    public class AgentChatBridge : MonoBehaviour
    {
        [SerializeField, Tooltip("Leave empty to use the only Agent in the scene.")]
        private Agent agent;

        [SerializeField, Tooltip("Leave empty to use the ChatBox on this GameObject.")]
        private ChatBox chatBox;

        [SerializeField, Tooltip("Optional name shown on this agent's messages, e.g. when several agents share one chat.")]
        private string label;

        [SerializeField] private bool showThoughts = true;
        [SerializeField] private bool showToolCalls = true;

        private void OnEnable()
        {
            if (chatBox == null) chatBox = GetComponent<ChatBox>();
            if (agent == null) agent = FindAnyObjectByType<Agent>();

            if (agent == null || chatBox == null)
            {
                Debug.LogError("[AgentChatBridge] No Agent in the scene or no ChatBox on this GameObject; assign them.", this);
                enabled = false;
                return;
            }

            chatBox.MessageSubmitted += OnSubmitted;
            agent.MessageSent += OnMessageSent;
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
            agent.MessageSent -= OnMessageSent;
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
                var url = Connector.BaseUrl;
                var healthy = await Connector.IsHealthy(destroyCancellationToken);
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

            _ = agent.Send(text);
        }

        // Every message given to the agent: the player's chat lines and game events (e.g. new orders).
        private void OnMessageSent(string text, string sender) =>
            chatBox.AddMessage(text, sender, "chat-message--user");

        private void OnThought(string thought)
        {
            if (showThoughts) chatBox.AddMessage(thought, Who("Thought"), "chat-message--thought");
        }

        private void OnAction(ActionCall action)
        {
            if (showToolCalls)
                chatBox.AddMessage($"{action.Tool} {action.Args?.ToString(Formatting.None) ?? "{}"}",
                    Who("Action"), "chat-message--action");
        }

        private void OnObservation(ActionCall action, ToolResult result)
        {
            if (showToolCalls)
                chatBox.AddMessage(result.Content, Who(result.IsError ? "Observation (failed)" : "Observation"),
                    result.IsError ? "chat-message--error" : "chat-message--observation");
        }

        private void OnFinal(string answer) => chatBox.AddMessage(answer, Who("Agent"), "chat-message--agent");
        private void OnFailed(string error) => chatBox.AddMessage(error, Who("Error"), "chat-message--error");
        private void OnCancelled() => Notice("Stopped.");

        private void OnStateChanged(AgentState state) => chatBox.SetStatus(state switch
        {
            AgentState.Thinking => "Thinking...",
            AgentState.RunningTool => "Running tool...",
            _ => null,
        });

        private void Notice(string text) => chatBox.AddMessage(text, null, "chat-message--system");

        private string Who(string sender) => string.IsNullOrEmpty(label) ? sender : $"{label} · {sender}";
    }
}
