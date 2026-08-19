using System;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// One level as data: the name of the scene to load, a display name, and the topology edges that
    /// lead out of it. A pure data asset following the SoundCue / FragmentCue approach — designers wire
    /// the level graph by dragging LevelScene assets into each other's connections, no code.
    /// Created via Assets > Create > Level > Level Scene, referenced by LevelFlow (the graph root) and
    /// by each other's connections.
    /// </summary>
    [CreateAssetMenu(menuName = "Level/Level Scene")]
    public class LevelScene : ScriptableObject
    {
        [Header("Scene")]
        [Tooltip("Scene file name, must match Build Settings exactly (Unity's SceneManager.LoadScene matches by name). Mismatched = a load that silently never resolves")]
        public string sceneName;

        [Tooltip("Human-readable name for menus (the save slots). Empty falls back to the scene name — filled by RoomBuilder from its room table")]
        public string displayName;

        /// <summary>The name to show a player. Falls back to sceneName, so an asset that never got a
        /// displayName still reads as something rather than as a blank row in the save menu.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? sceneName : displayName;

        [Header("Topology")]
        [Tooltip("The player may leave this level through any of these connections. A level with several exits (e.g. a normal door and a secret door) lists them all; the in-scene LevelExit trigger names which one it is by its id")]
        public LevelConnection[] connections;

        /// <summary>
        /// The level a connection leads to. Returns null when the id is unknown or the connection's
        /// target is empty (the caller — SceneDirector — decides the fallback: a warning and back to
        /// the menu).
        /// </summary>
        public LevelScene TargetOf(string exitId)
        {
            if (connections == null) return null;

            // Linear scan: a level has a handful of connections, not hundreds; a dictionary is not worth it
            foreach (LevelConnection c in connections)
            {
                if (c != null && string.Equals(c.id, exitId, StringComparison.Ordinal)) return c.target;
            }
            return null;
        }
    }

    /// <summary>
    /// A directed edge in the level graph: "through exit id you reach target". The id is the contract
    /// between this asset and the LevelExit trigger placed in the scene — they must spell it identically.
    /// </summary>
    [Serializable]
    public class LevelConnection
    {
        [Tooltip("Identifier matching a LevelExit trigger's exitId in this level's scene")]
        public string id;

        [Tooltip("The level to load when the player leaves through this exit. Empty = a dead end (SceneDirector falls back)")]
        public LevelScene target;
    }
}
