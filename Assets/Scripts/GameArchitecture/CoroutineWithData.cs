using System.Collections;
using UnityEngine;

namespace GameArchitecture
{
    public class CoroutineWithData
    {
        private readonly IEnumerator target;
        public object Result;

        public CoroutineWithData(MonoBehaviour owner, IEnumerator target)
        {
            this.target = target;
            coroutine = owner.StartCoroutine(Run());
        }

        public Coroutine coroutine { get; private set; }

        private IEnumerator Run()
        {
            while (target.MoveNext())
            {
                Result = target.Current;
                yield return Result;
            }
        }
    }
}