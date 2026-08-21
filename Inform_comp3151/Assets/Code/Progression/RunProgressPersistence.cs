using Inkform.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.Progression
{
    /// <summary>Debounces high-frequency progression changes before writing the active save slot.</summary>
    public sealed class RunProgressPersistence : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float saveDelay = 0.35f;
        private bool dirty;
        private float saveAt;

        private void OnEnable()
        {
            RunProgressStore.Changed += MarkDirty;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            RunProgressStore.Changed -= MarkDirty;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Flush();
        }

        private void Update()
        {
            if (dirty && Time.unscaledTime >= saveAt) Flush();
        }

        private void MarkDirty()
        {
            dirty = true;
            saveAt = Time.unscaledTime + saveDelay;
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene) => Flush();
        private void OnApplicationPause(bool paused) { if (paused) Flush(); }
        private void OnApplicationQuit() => Flush();

        public void Flush()
        {
            if (!dirty) return;
            dirty = false;
            SaveStore.SaveNow();
        }
    }
}
