#if UNITY_EDITOR
using Inkform.Life;
using Inkform.Player;
using UnityEditor;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>
    /// One-shot migration tool: completes the asset and prefab wiring needed by the "death strategy +
    /// level memento + PlayerHandler split" refactor in one go.
    /// Menu: Tools/Refactor/Apply Death + Split Migration.
    ///
    /// Why this script must exist: when splitting components, Unity does **not** migrate
    /// [SerializeField] values onto the new components, and Player.prefab has 11 configured values and
    /// 4 child-object references that differ from the code defaults. Re-wiring by hand is slow and
    /// easy to miss, and a miss only shows as "the feel is slightly off", extremely costly to debug.
    ///
    /// Idempotent: repeated runs add no duplicate components and create no duplicate assets (same
    /// approach as PlayerAnimBuilder — update in place when it exists).
    /// </summary>
    public static class RefactorMigration
    {
        const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        const string GameManagerPrefab = "Assets/Prefabs/GameManager.prefab";

        const string StrategyDir = "Assets/Life";
        const string SpikeStrategy = "Assets/Life/DeathStrategy_Spike.asset";

        const string PlayerPieces = "Assets/Fx/FX_PlayerPieces.asset";
        const string DeathSound = "Assets/Audio/SFX/Player/SFX_PlayerDead.asset";

        // Names of the four probe child objects in Player.prefab. ContactSensor's references are
        // rewired by name — before the split they were PlayerHandler fields, and changing the code
        // made Unity drop those references
        const string GroundChild = "CheckGround";
        const string CeilingChild = "CheckCeiling";
        const string LeftWallChild = "CheckLeftWall";
        const string RightWallChild = "CheckRightWall";

        [MenuItem("Tools/Refactor/Apply Death + Split Migration")]
        public static void Apply()
        {
            DeathStrategy strategy = EnsureSpikeStrategy();
            MigrateGameManager(strategy);
            MigratePlayer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Refactor migration complete: death strategy asset, GameManager and Player prefabs all wired.");
        }

        // ---- Death strategy asset ----

        private static DeathStrategy EnsureSpikeStrategy()
        {
            if (!AssetDatabase.IsValidFolder(StrategyDir))
                AssetDatabase.CreateFolder("Assets", "Life");

            var strategy = AssetDatabase.LoadAssetAtPath<ShatterDeathStrategy>(SpikeStrategy);
            if (strategy == null)
            {
                strategy = ScriptableObject.CreateInstance<ShatterDeathStrategy>();
                AssetDatabase.CreateAsset(strategy, SpikeStrategy);
            }

            // Values come from the pre-refactor FxDirector Death FX section and PlayerDeathFx,
            // matching Player_test.unity's actual values one by one — the migration must not change the feel
            var so = new SerializedObject(strategy);
            SetEnum(so, "cause", (int)DeathCause.Spike);
            SetFloat(so, "respawnDelay", 0.9f);
            SetRef(so, "pieces", AssetDatabase.LoadAssetAtPath<Object>(PlayerPieces));
            SetFloat(so, "burstForce", 14f);
            SetFloat(so, "trauma", 0.7f);
            SetFloat(so, "hitStop", 0.12f);
            SetFloat(so, "zoom", -0.5f);
            SetFloat(so, "zoomTime", 0.4f);
            SetFloat(so, "punch", 0.8f);
            SetFloat(so, "punchTime", 0.5f);
            SetRef(so, "deathCue", AssetDatabase.LoadAssetAtPath<Object>(DeathSound));
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(strategy);
            return strategy;
        }

        // ---- GameManager: add death director + level memento ----

        private static void MigrateGameManager(DeathStrategy strategy)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefab);
            if (root == null) { Debug.LogError($"Cannot find {GameManagerPrefab}"); return; }

            DeathDirector deaths = Ensure<DeathDirector>(root);
            var so = new SerializedObject(deaths);

            // Only one cause has a strategy; the array is fixed at length 1
            SerializedProperty arr = so.FindProperty("strategies");
            if (arr != null)
            {
                arr.arraySize = 1;
                arr.GetArrayElementAtIndex(0).objectReferenceValue = strategy;
            }
            SetRef(so, "fallback", strategy);   // fallback uses it too: an unconfigured cause still gets a presentation
            so.ApplyModifiedPropertiesWithoutUndo();

            Ensure<LevelMemento>(root);         // no wiring needed, it scans the scene itself

            PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---- Player: add the four split components and rewire the probe points ----

        private static void MigratePlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            if (root == null) { Debug.LogError($"Cannot find {PlayerPrefab}"); return; }

            // Order does not matter, but RequireComponent would add them anyway; adding explicitly
            // first so nothing is missed
            ContactSensor sensor = Ensure<ContactSensor>(root);
            Ensure<PlayerMotor>(root);
            Ensure<AnimStateResolver>(root);
            Ensure<ItemCarrier>(root);

            var so = new SerializedObject(sensor);
            SetRef(so, "groundCheck", FindChild(root.transform, GroundChild));
            SetRef(so, "leftWallCheck", FindChild(root.transform, LeftWallChild));
            SetRef(so, "rightWallCheck", FindChild(root.transform, RightWallChild));
            SetRef(so, "ceilingCheck", FindChild(root.transform, CeilingChild));
            so.ApplyModifiedPropertiesWithoutUndo();

            // The remaining values are not set here: the new components' C# defaults already equal
            // the prefab's original values (movingSpeed 10 / jumpSpeed 12 / CheckRadius 0.1 ...),
            // adding them would be redundant

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---- Small helpers ----

        private static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // Iterate rather than Transform.Find: the latter requires the full hierarchy path, and would
        // silently fail once a child is moved into an empty node
        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            Debug.LogWarning($"Cannot find child {name} in Player.prefab; the matching probe needs a manual drag");
            return null;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Warn(so, field); return; }
            p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Warn(so, field); return; }
            p.floatValue = value;
        }

        private static void SetEnum(SerializedObject so, string field, int value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Warn(so, field); return; }
            p.enumValueIndex = value;
        }

        // This script fails silently after field renames, so a missing field must complain
        private static void Warn(SerializedObject so, string field)
            => Debug.LogWarning($"Serialized field {field} not found on {so.targetObject.GetType().Name}, skipped");
    }
}
#endif
