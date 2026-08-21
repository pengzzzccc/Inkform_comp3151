using UnityEngine;

namespace Inkform.Core
{
    /// <summary>Claims the persistent GameManager before any sibling manager can initialize.</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class PersistentGameRoot : MonoBehaviour
    {
        public static PersistentGameRoot Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
