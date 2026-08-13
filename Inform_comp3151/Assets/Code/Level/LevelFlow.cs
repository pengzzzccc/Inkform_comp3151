using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// The root of the level graph (the "world map"): names the menu scene, the level a new run starts
    /// in, and every level that belongs to the game. SceneDirector holds the one instance of this asset
    /// and resolves all transitions through it — the single source of truth for "which scene is the
    /// menu" and "where does this level lead".
    /// Created via Assets > Create > Level > Level Flow.
    /// </summary>
    [CreateAssetMenu(menuName = "Level/Level Flow")]
    public class LevelFlow : ScriptableObject
    {
        [Tooltip("The menu scene file name, must match Build Settings exactly (index 0 — the startup scene)")]
        public string mainMenuSceneName = "MainMenu";

        [Header("Levels")]
        [Tooltip("The level a fresh run starts in. Empty = no playable game yet; StartNewGame warns")]
        public LevelScene entryLevel;

        [Tooltip("Every level in the game. Feed a future level-select panel and let editor tooling validate that each one's scene is in Build Settings")]
        public LevelScene[] levels;

        /// <summary>The LevelScene asset whose sceneName matches, or null. Used by SceneDirector on
        /// scene load to recover "which level is this" from the scene name alone.</summary>
        public LevelScene FindBySceneName(string sceneName)
        {
            if (levels == null || string.IsNullOrEmpty(sceneName)) return null;

            foreach (LevelScene l in levels)
            {
                if (l != null && l.sceneName == sceneName) return l;
            }
            return null;
        }

        /// <summary>Whether a scene name is the menu scene (the one that shows the main menu). UIManager
        /// asks this instead of holding its own scene-name string, so the policy lives in one place.</summary>
        public bool IsMenuScene(string sceneName)
        {
            return string.Equals(sceneName, mainMenuSceneName, System.StringComparison.Ordinal);
        }
    }
}
