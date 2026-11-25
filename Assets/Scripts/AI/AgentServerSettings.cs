using UnityEngine;

namespace AI
{
    /// <summary>
    /// Optional settings for <see cref="Connector"/>. To override the defaults, create one with
    /// Create → AI → Agent Server Settings and save it as "AgentServerSettings" in any Resources folder
    /// (e.g. Assets/Resources/AgentServerSettings.asset). Without it, localhost:5000 is used.
    /// </summary>
    [CreateAssetMenu(fileName = Connector.SettingsResourcePath, menuName = "AI/Agent Server Settings")]
    public class AgentServerSettings : ScriptableObject
    {
        [SerializeField] private string baseUrl = Connector.DefaultBaseUrl;

        [SerializeField, Min(1), Tooltip("The server waits up to 120 s for the model, so keep this above that.")]
        private int timeoutSeconds = Connector.DefaultTimeoutSeconds;

        public string BaseUrl => baseUrl;
        public int TimeoutSeconds => timeoutSeconds;
    }
}
