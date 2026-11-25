using System;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace Demo.Shared
{
    public enum MoveResult
    {
        Arrived,
        Unreachable,
        TimedOut,
    }

    /// <summary>
    /// The character's game logic: walking on the NavMesh (a flat floor, so movement stays on the
    /// XZ plane) and talking. Knows nothing about the agent; each demo exposes it through its own [AgentTool] component.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class DemoCharacter : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float moveTimeoutSeconds = 20f;
        [SerializeField] private Vector3 speechOffset = new(0f, 1.5f, 0f);

        private NavMeshAgent _nav;
        private Label _bubble;
        private float _bubbleHideAt;

        public bool IsMoving => _nav.hasPath || _nav.pathPending;

        private void Awake() => _nav = GetComponent<NavMeshAgent>();

        /// <summary>Walks to the closest NavMesh point to <paramref name="destination"/>. Cancelling stops on the spot.</summary>
        public async Awaitable<MoveResult> MoveTo(Vector3 destination, CancellationToken ct)
        {
            if (!NavMesh.SamplePosition(destination, out var hit, 2f, NavMesh.AllAreas))
                return MoveResult.Unreachable;

            var path = new NavMeshPath();
            if (!_nav.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return MoveResult.Unreachable;

            _nav.SetPath(path);
            var deadline = Time.time + moveTimeoutSeconds;
            try
            {
                while (_nav.pathPending || _nav.remainingDistance > _nav.stoppingDistance + 0.05f)
                {
                    if (Time.time > deadline)
                    {
                        _nav.ResetPath();
                        return MoveResult.TimedOut;
                    }

                    await Awaitable.NextFrameAsync(ct);
                }

                return MoveResult.Arrived;
            }
            catch (OperationCanceledException)
            {
                if (_nav != null && _nav.isOnNavMesh) _nav.ResetPath();
                throw;
            }
        }

        /// <summary>Shows a speech bubble above the character for a few seconds.</summary>
        public void Say(string text)
        {
            var overlay = WorldOverlay.Current;
            if (overlay == null) return;

            overlay.Remove(_bubble);
            _bubble = overlay.AddLabel(transform, speechOffset, text, WorldOverlay.LabelKind.Speech);
            _bubbleHideAt = Time.time + Mathf.Clamp(text.Length * 0.08f, 2.5f, 8f);
        }

        private void Update()
        {
            if (_bubble != null && Time.time >= _bubbleHideAt)
            {
                if (WorldOverlay.Current != null) WorldOverlay.Current.Remove(_bubble);
                _bubble = null;
            }
        }
    }
}
