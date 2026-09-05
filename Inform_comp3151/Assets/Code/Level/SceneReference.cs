using System;
using System.IO;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// A scene referenced the way Unity wants it: an asset drag in the editor, a serialized path in
    /// the build. SceneAsset is an editor-only type, so the field is a plain Object here — the
    /// property drawer (Editor/World) offers the SceneAsset object field, and this class keeps the
    /// path string in sync (the asset wins on save; the path recovers the asset if the reference was
    /// ever lost). Renaming or moving the scene in the editor keeps the reference alive, which a
    /// bare path string never does.
    ///
    /// SceneName — the file name without extension — remains this project's room identity: saves
    /// hold it, LevelBus.Started broadcasts it, LoadScene accepts it. Paths are only plumbing.
    /// </summary>
    [Serializable]
    public sealed class SceneReference : ISerializationCallbackReceiver
    {
        // UnityEngine.Object spelled out: this file also imports System (for Serializable), and a
        // bare "Object" is ambiguous between the two
        [SerializeField] private UnityEngine.Object sceneAsset;      // a SceneAsset in the editor; null in builds
        [SerializeField] private string scenePath = "";  // the build-side truth

        public bool IsSet => sceneAsset != null || !string.IsNullOrEmpty(scenePath);

        /// <summary>The scene's file name without extension. Unity accepts it in LoadScene, and it
        /// is the save/room identity everywhere else.</summary>
        public string SceneName => Path.GetFileNameWithoutExtension(scenePath);

        public string ScenePath => scenePath;

#if UNITY_EDITOR
        /// <summary>Editor-only typed view for the drawer, validator and build syncing.</summary>
        public UnityEditor.SceneAsset SceneAsset => sceneAsset as UnityEditor.SceneAsset;
#endif

        // AssetDatabase is editor-only, so the sync only exists there; in a build OnBeforeSerialize
        // is a no-op and the path written last time in the editor is what ships
        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            if (sceneAsset != null)
            {
                scenePath = UnityEditor.AssetDatabase.GetAssetPath(sceneAsset);
            }
            else if (!string.IsNullOrEmpty(scenePath))
            {
                // The reference was lost (a merge, a script recompile wiping the object) but the
                // path survived — re-link instead of silently becoming an empty reference
                sceneAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scenePath);
            }
#endif
        }

        public void OnAfterDeserialize() { }
    }
}
