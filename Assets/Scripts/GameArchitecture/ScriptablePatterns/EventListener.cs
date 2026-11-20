using UnityEngine;
using UnityEngine.Events;

namespace GameArchitecture.ScriptablePatterns
{
    public class EventListener : MonoBehaviour
    {
        [Header("Events")] [SerializeField] public GameEvent gameEvent;

        [SerializeField] private Event localEvent = new();

        private void Awake()
        {
            gameEvent.Listen(this);
        }

        private void OnDisable()
        {
            gameEvent.Deafen(this);
        }

        private void OnDestroy()
        {
            gameEvent.Deafen(this);
        }

        public void Invoke()
        {
            localEvent.Invoke();
        }

        public void AddListener(UnityAction call)
        {
            localEvent.AddListener(call);
        }
    }
}