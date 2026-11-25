using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GameArchitecture.ScriptablePatterns
{
    [Serializable]
    public class Event : UnityEvent
    {
    }

    [Serializable]
    public class FloatEvent : UnityEvent<float>
    {
    }


    [CreateAssetMenu(fileName = "GameEvent", menuName = "GameEvents/GameEvent", order = 0)]
    public class GameEvent : ScriptableObject
    {
        private readonly HashSet<EventListener> mEventListeners = new();
        private readonly ConcurrentQueue<EventListener> mRemoveQueue = new();

        public void Listen(EventListener listener)
        {
            if (mEventListeners.Contains(listener)) return;

            mEventListeners.Add(listener);
        }

        public void Deafen(EventListener listener)
        {
            if (!mEventListeners.Contains(listener)) return;

            mRemoveQueue.Enqueue(listener);
        }

        public void Invoke()
        {
            foreach (var listener in mEventListeners) listener.Invoke();
            CleanQueue();
        }

        private void CleanQueue()
        {
            while (!mRemoveQueue.IsEmpty)
            {
                EventListener listener;
                if (mRemoveQueue.TryDequeue(out listener)) mEventListeners.Remove(listener);
            }
        }
    }
}