using UnityEngine;

namespace GameArchitecture.ScriptablePatterns
{
    public abstract class ScriptableVar<T> : ScriptableObject
    {
        [SerializeField] private T defaultValue;
        [SerializeField] private bool isConstant;
        [HideInInspector] public bool hasValueChanged;

        private T m_Value;

        public T Value
        {
            get
            {
                if (isConstant) return defaultValue;
                hasValueChanged = false;
                return m_Value;
            }
            set
            {
                if (isConstant) return;
                hasValueChanged = true;
                m_Value = value;
            }
        }
    }
}