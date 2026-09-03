using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// The world as one asset: which scene is the main menu, which room a new game starts in, and
    /// the registry of every room. This replaces LevelGraph.txt — the room-to-room topology itself
    /// now lives on the doors (each LevelExit references its destination RoomDefinition directly),
    /// so this asset is only the lookups: menu/entry for SceneDirector's flow calls, and
    /// rooms-by-scene-name for continuing saves.
    ///
    /// Build Settings are derived state: Tools > Inkform > World > Sync Build Settings puts the menu
    /// scene first and every registered room after it, so a room missing from the build cannot
    /// happen quietly anymore.
    /// </summary>
    [CreateAssetMenu(menuName = "Inkform/World Definition", fileName = "World")]
    public sealed class WorldDefinition : ScriptableObject
    {
        [Tooltip("The main menu scene")]
        [SerializeField] private SceneReference menuScene = new SceneReference();

        [Tooltip("The room a New Game starts in")]
        [SerializeField] private RoomDefinition entryRoom;

        [Tooltip("Every room of the world. Doors hold their own destination references; this list is the registry for save lookups and build syncing")]
        [SerializeField] private List<RoomDefinition> rooms = new List<RoomDefinition>();

        public SceneReference MenuScene => menuScene;
        public RoomDefinition EntryRoom => entryRoom;
        public IReadOnlyList<RoomDefinition> Rooms => rooms;

        public string MenuSceneName => menuScene.SceneName;

        /// <summary>The room whose scene name matches, or null — used to validate save continuation
        /// and to resolve the active scene back to data. A scene name is the identity, so this is a
        /// plain comparison; a room missing from the list behaves as "not part of the world".</summary>
        public RoomDefinition FindBySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            foreach (RoomDefinition room in rooms)
                if (room != null && room.SceneName == sceneName) return room;
            return null;
        }
    }
}
