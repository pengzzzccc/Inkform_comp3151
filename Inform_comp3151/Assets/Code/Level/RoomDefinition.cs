using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// One room of the world as an asset: the scene it lives in, plus the display name the save
    /// menu shows. Room identity is the scene's file name (SceneName) — that is what
    /// SaveData.sceneName has always held, so renaming a room scene stays a save-breaking change,
    /// same as it ever was.
    ///
    /// Doors (LevelExit) reference these directly; WorldDefinition lists them as the registry for
    /// save-continuation lookups and build syncing. Create via Create > Inkform > Room Definition,
    /// or let the migration tool build them from the old text graph.
    /// </summary>
    [CreateAssetMenu(menuName = "Inkform/Room Definition", fileName = "Room")]
    public sealed class RoomDefinition : ScriptableObject
    {
        [Tooltip("The scene this room lives in")]
        [SerializeField] private SceneReference scene = new SceneReference();

        [Tooltip("Shown in the save menu; falls back to the scene name when empty")]
        [SerializeField] private string displayName = "";

        public SceneReference Scene => scene;
        public string SceneName => scene.SceneName;
        public bool IsSet => scene.IsSet;

        public string DisplayName =>
            string.IsNullOrEmpty(displayName) || string.IsNullOrWhiteSpace(scene.SceneName)
                ? scene.SceneName
                : displayName;
    }
}
