using System.Collections;
using UnityEngine;

namespace GameArchitecture
{
    public class CoroutineHandle : IEnumerator
    {
        public CoroutineHandle(MonoBehaviour owner, IEnumerator coroutine)
        {
            Current = owner.StartCoroutine(Wrap(coroutine));
        }

        public bool IsDone { get; private set; }

        public bool MoveNext()
        {
            return !IsDone;
        }

        public void Reset()
        {
        }

        public object Current { get; }

        private IEnumerator Wrap(IEnumerator coroutine)
        {
            yield return coroutine;
            IsDone = true;
        }
    }
}