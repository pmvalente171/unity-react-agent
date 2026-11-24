using System;
using System.Collections.Generic;
using System.Threading;
using AI.Contract;
using AI.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI
{
    public enum AgentState
    {
        Idle,
        Thinking,    // waiting for the server
        RunningTool, // a tool is executing
    }

    /// <summary>
    /// Runs the ReAct loop on the Unity side: owns the history, asks the server for one step
    /// at a time through the <see cref="Connector"/>, runs the chosen tool from its
    /// <see cref="ToolSet"/>, and reports every step through C# events.
    /// </summary>
    public class Agent : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;
        [SerializeField, Min(1)] private int maxSteps = 10;

        private readonly List<Message> _history = new();
        private CancellationTokenSource _cts;

        public IReadOnlyList<Message> History => _history;
        public AgentState State { get; private set; } = AgentState.Idle;
        public bool IsBusy => State != AgentState.Idle;

        public event Action<string> ThoughtReceived;
        public event Action<ActionCall> ActionStarted;
        public event Action<ActionCall, ToolResult> ObservationReceived;
        public event Action<string> FinalAnswer;
        public event Action<string> Failed;
        public event Action Cancelled;
        public event Action<AgentState> StateChanged;

        /// <summary>Adds a user message and runs the loop until a final answer, an error or a stop.</summary>
        public async Awaitable Send(string userMessage)
        {
            if (IsBusy)
            {
                Debug.LogWarning("[Agent] Already running; ignoring the new message.", this);
                return;
            }

            if (!Connector.HasInstance)
            {
                Failed?.Invoke("No Connector in the scene.");
                return;
            }

            _history.Add(Message.User(userMessage));
            _cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            var ct = _cts.Token;

            try
            {
                for (var step = 0; step < maxSteps; step++)
                {
                    SetState(AgentState.Thinking);
                    var response = await RequestStep(ct);

                    if (!string.IsNullOrEmpty(response.Thought))
                        ThoughtReceived?.Invoke(response.Thought);

                    if (response.IsFinal)
                    {
                        _history.Add(Message.AssistantFinal(response.Thought, response.Answer));
                        FinalAnswer?.Invoke(response.Answer ?? "");
                        return;
                    }

                    var action = response.Action;
                    _history.Add(Message.AssistantAction(response.Thought, action));
                    ActionStarted?.Invoke(action);

                    SetState(AgentState.RunningTool);
                    ToolResult result;
                    try
                    {
                        result = await RunTool(action, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        // Every action needs an observation, or the next request is rejected.
                        _history.Add(Message.Observation(action.Id, "Stopped by the player before it finished.", true));
                        throw;
                    }

                    _history.Add(Message.Observation(action.Id, result.Content, result.IsError));
                    ObservationReceived?.Invoke(action, result);
                }

                Failed?.Invoke($"Stopped after {maxSteps} steps without a final answer.");
            }
            catch (OperationCanceledException)
            {
                Cancelled?.Invoke();
            }
            catch (StepException e)
            {
                Failed?.Invoke(e.Message);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                Failed?.Invoke(e.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetState(AgentState.Idle);
            }
        }

        /// <summary>Stops the current run. The running tool gets its CancellationToken cancelled.</summary>
        public void Cancel() => _cts?.Cancel();

        /// <summary>Forgets the conversation. Only while idle.</summary>
        public bool ResetConversation()
        {
            if (IsBusy) return false;
            _history.Clear();
            return true;
        }

        private async Awaitable<StepResponse> RequestStep(CancellationToken ct)
        {
            while (true)
            {
                var request = new StepRequest
                {
                    Tools = toolSet != null ? toolSet.ToSpecs() : new List<ToolSpec>(),
                    Messages = _history,
                };

                try
                {
                    return await Connector.Instance.Step(request, ct);
                }
                catch (StepException e) when (e.Code == ErrorCodes.ContextTooLong && TrimOldestTurn())
                {
                    Debug.Log("[Agent] History too long for the model; dropped the oldest turn and retrying.", this);
                }
            }
        }

        private async Awaitable<ToolResult> RunTool(ActionCall action, CancellationToken ct)
        {
            if (toolSet == null || !toolSet.TryGet(action.Tool, out var tool))
                return ToolResult.Fail($"Tool '{action.Tool}' is not available right now.");

            try
            {
                return await tool.Execute(action.Args ?? new JObject(), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return ToolResult.Fail($"Tool '{action.Tool}' failed: {e.Message}");
            }
        }

        /// <summary>
        /// CONTRACT.md "Trimming history": keep the first user message, drop the oldest turn after
        /// it, and always drop an action together with its observation. False when nothing can go.
        /// </summary>
        private bool TrimOldestTurn()
        {
            if (_history.Count < 3) return false;

            var count = 1;
            var first = _history[1];
            if (first.IsAssistantAction &&
                _history[2].Role == Roles.Observation && _history[2].ActionId == first.Action.Id)
                count = 2;

            if (1 + count >= _history.Count) return false; // would leave nothing after the task
            _history.RemoveRange(1, count);
            return true;
        }

        private void SetState(AgentState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
