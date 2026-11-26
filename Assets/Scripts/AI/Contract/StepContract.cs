using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// C# mirror of the /step wire format. The source of truth is agent_server/CONTRACT.md:
// change that first (and tell the backend session), then update these types.
namespace AI.Contract
{
    public static class Roles
    {
        public const string User = "user";
        public const string Assistant = "assistant";
        public const string Observation = "observation";
    }

    public static class ErrorCodes
    {
        // From the server (see CONTRACT.md "Errors").
        public const string BadRequest = "bad_request";
        public const string ContextTooLong = "context_too_long";
        public const string LlmError = "llm_error";
        public const string AgentError = "agent_error";
        public const string NotSupported = "not_supported";

        // Client-side only.
        public const string Network = "network_error";
        public const string InvalidResponse = "invalid_response";
    }

    public class ToolSpec
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("parameters")] public JObject Parameters; // JSON Schema; null = no arguments
    }

    public class ActionCall
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("tool")] public string Tool;
        [JsonProperty("args")] public JObject Args;
    }

    /// <summary>One history entry. Use the factory methods; unused fields stay null and aren't sent.</summary>
    public class Message
    {
        [JsonProperty("role")] public string Role;

        // user / observation
        [JsonProperty("content")] public string Content;

        // assistant
        [JsonProperty("thought")] public string Thought;
        [JsonProperty("action")] public ActionCall Action;
        [JsonProperty("answer")] public string Answer;

        // observation
        [JsonProperty("action_id")] public string ActionId;
        [JsonProperty("is_error")] public bool? IsError;

        [JsonIgnore] public bool IsAssistantAction => Role == Roles.Assistant && Action != null;

        public static Message User(string content) =>
            new() { Role = Roles.User, Content = content };

        public static Message AssistantAction(string thought, ActionCall action) =>
            new() { Role = Roles.Assistant, Thought = thought ?? "", Action = action };

        public static Message AssistantFinal(string thought, string answer) =>
            new() { Role = Roles.Assistant, Thought = thought ?? "", Answer = answer ?? "" };

        public static Message Observation(string actionId, string content, bool isError) =>
            new() { Role = Roles.Observation, ActionId = actionId, Content = content ?? "", IsError = isError ? true : null };
    }

    public class StepRequest
    {
        [JsonProperty("tools")] public List<ToolSpec> Tools;
        [JsonProperty("messages")] public List<Message> Messages;

        // Optional per-agent instructions, added to the server's own system prompt. Null = not sent.
        [JsonProperty("instructions", NullValueHandling = NullValueHandling.Ignore)] public string Instructions;
    }

    public class StepResponse
    {
        public const string TypeAction = "action";
        public const string TypeFinal = "final";

        [JsonProperty("type")] public string Type;
        [JsonProperty("thought")] public string Thought;
        [JsonProperty("action")] public ActionCall Action;
        [JsonProperty("answer")] public string Answer;

        [JsonIgnore] public bool IsFinal => Type == TypeFinal;
    }

    public class ErrorBody
    {
        [JsonProperty("error")] public string Error;
        [JsonProperty("code")] public string Code;
    }

    /// <summary>Any failed /step or /health call. <see cref="Status"/> is 0 when no HTTP response arrived.</summary>
    public class StepException : Exception
    {
        public int Status { get; }
        public string Code { get; }

        public StepException(int status, string code, string message) : base(message)
        {
            Status = status;
            Code = code;
        }
    }
}
