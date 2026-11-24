using System.Text;
using System.Threading;
using AI.Contract;
using GameArchitecture;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace AI
{
    /// <summary>
    /// HTTP client for the agent server. Only knows the wire format (AI.Contract),
    /// nothing about the loop or tools. Every failure becomes a <see cref="StepException"/>.
    /// </summary>
    public class Connector : Singleton<Connector>
    {
        [SerializeField] private string baseUrl = "http://localhost:5000";

        [SerializeField, Min(1), Tooltip("The server waits up to 120 s for the model, so keep this above that.")]
        private int timeoutSeconds = 130;

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        public string BaseUrl
        {
            get => baseUrl;
            set => baseUrl = value;
        }

        /// <summary>POST /step: ask for the next ReAct step.</summary>
        public async Awaitable<StepResponse> Step(StepRequest request, CancellationToken ct = default)
        {
            var body = JsonConvert.SerializeObject(request, JsonSettings);
            var text = await Send("POST", "/step", body, ct);

            StepResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<StepResponse>(text);
            }
            catch (JsonException e)
            {
                throw new StepException(200, ErrorCodes.InvalidResponse, $"Unreadable /step response: {e.Message}");
            }

            var valid = response != null && (response.IsFinal ||
                        (response.Type == StepResponse.TypeAction && response.Action is { Id: not null, Tool: not null }));
            if (!valid)
                throw new StepException(200, ErrorCodes.InvalidResponse, $"Unexpected /step response: {text}");

            return response;
        }

        /// <summary>GET /health: true when the server answers.</summary>
        public async Awaitable<bool> IsHealthy(CancellationToken ct = default)
        {
            try
            {
                await Send("GET", "/health", null, ct);
                return true;
            }
            catch (StepException)
            {
                return false;
            }
        }

        private async Awaitable<string> Send(string method, string path, string jsonBody, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); // Register() would abort before the send even starts

            UnityWebRequest created;
            try
            {
                created = new UnityWebRequest(baseUrl.TrimEnd('/') + path, method);
            }
            catch (System.Exception e) when (e is System.ArgumentException or System.UriFormatException)
            {
                throw new StepException(0, ErrorCodes.Network, $"Invalid agent server URL '{baseUrl}': {e.Message}");
            }

            using var request = created;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            using (ct.Register(request.Abort))
                await request.SendWebRequest();

            ct.ThrowIfCancellationRequested();

            var text = request.downloadHandler.text;
            if (request.result == UnityWebRequest.Result.Success)
                return text;

            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                var status = (int)request.responseCode;
                ErrorBody error = null;
                try { error = JsonConvert.DeserializeObject<ErrorBody>(text); }
                catch (JsonException) { /* not a contract error body */ }

                throw new StepException(status, error?.Code ?? $"http_{status}", error?.Error ?? $"HTTP {status}: {text}");
            }

            throw new StepException(0, ErrorCodes.Network, $"Can't reach the agent server at {baseUrl}: {request.error}");
        }
    }
}
