using UnityEngine;

namespace GameArchitecture
{
    /// <summary>
    /// Scene-placed MonoBehaviour singleton. Put one instance in the scene;
    /// any duplicates destroy themselves on Awake.
    /// Usage: <c>public class Foo : Singleton&lt;Foo&gt; { }</c>
    /// Subclasses overriding Awake/OnDestroy must call the base implementation.
    /// </summary>
    public abstract class Singleton<T> : MonoBehaviour where T : Singleton<T>
    {
        [SerializeField] private bool persistAcrossScenes;

        private static T _instance;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                    Debug.LogError($"[Singleton] No instance of {typeof(T).Name} in the scene.");
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[Singleton] Duplicate {typeof(T).Name} on '{name}' destroyed.");
                Destroy(gameObject);
                return;
            }

            _instance = (T)this;

            if (persistAcrossScenes)
            {
                transform.SetParent(null); // DontDestroyOnLoad only works on root objects
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
