#if UNITY_EDITOR
using Inkform.Life;
using Inkform.Player;
using UnityEditor;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>
    /// 一次性迁移工具：把「死亡策略 + 关卡备忘录 + PlayerHandler 拆分」这轮重构所需的
    /// 资产和预制体接线一次做完。菜单：Tools/Refactor/Apply Death + Split Migration。
    ///
    /// 为什么必须有这个脚本：拆组件时 Unity **不会**把 [SerializeField] 的值迁到新组件上，
    /// 而 Player.prefab 上有 11 个配值和 4 个子物体引用与代码默认值不同。
    /// 手工重接一遍既慢又容易漏，漏了还只表现为「手感有点怪」，排查成本极高。
    ///
    /// 幂等：重复点击不会重复加组件、也不会重复建资产（沿用 PlayerAnimBuilder 的做法，
    /// 已存在就原地更新）。
    /// </summary>
    public static class RefactorMigration
    {
        const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        const string GameManagerPrefab = "Assets/Prefabs/GameManager.prefab";

        const string StrategyDir = "Assets/Life";
        const string SpikeStrategy = "Assets/Life/DeathStrategy_Spike.asset";

        const string PlayerPieces = "Assets/Fx/FX_PlayerPieces.asset";
        const string DeathSound = "Assets/Audio/SFX/Player/SFX_PlayerDead.asset";

        // Player.prefab 里四个探测点子物体的名字。ContactSensor 的引用靠名字重接 ——
        // 拆分前它们是 PlayerHandler 的字段，代码一改 Unity 就把那几个引用丢了
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
            Debug.Log("重构迁移完成：死亡策略资产、GameManager 与 Player 预制体均已接线。");
        }

        // ---- 死亡策略资产 ----

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

            // 数值取自重构前 FxDirector 的 Death FX 区段和 PlayerDeathFx，
            // 与 Player_test.unity 里的实配值逐项一致 —— 迁移不该改变手感
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

        // ---- GameManager：加死亡导演 + 关卡备忘录 ----

        private static void MigrateGameManager(DeathStrategy strategy)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefab);
            if (root == null) { Debug.LogError($"找不到 {GameManagerPrefab}"); return; }

            DeathDirector deaths = Ensure<DeathDirector>(root);
            var so = new SerializedObject(deaths);

            // 只有一个死因有策略，数组固定长度 1
            SerializedProperty arr = so.FindProperty("strategies");
            if (arr != null)
            {
                arr.arraySize = 1;
                arr.GetArrayElementAtIndex(0).objectReferenceValue = strategy;
            }
            SetRef(so, "fallback", strategy);   // 兜底也用它：漏配的死因至少还有一套演出
            so.ApplyModifiedPropertiesWithoutUndo();

            Ensure<LevelMemento>(root);         // 无需接线，自己扫场景

            PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---- Player：加四个拆出来的组件并重接探测点 ----

        private static void MigratePlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            if (root == null) { Debug.LogError($"找不到 {PlayerPrefab}"); return; }

            // 顺序无所谓，但 RequireComponent 会连带自动加，先显式加一遍免得漏
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

            // 其余数值不在这里设：新组件的 C# 默认值已经写成了预制体上的原配值
            // （movingSpeed 10 / jumpSpeed 12 / CheckRadius 0.1 …），加上就是对的

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---- 小工具 ----

        private static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // 用遍历而不是 Transform.Find：后者要求写出完整层级路径，
        // 子物体一旦被挪进空节点里就会静默失配
        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            Debug.LogWarning($"Player.prefab 里找不到子物体 {name}，对应的探测点需要手动拖");
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

        // 字段改名后本脚本会静默失效，所以找不到时必须吭一声
        private static void Warn(SerializedObject so, string field)
            => Debug.LogWarning($"{so.targetObject.GetType().Name} 上找不到序列化字段 {field}，已跳过");
    }
}
#endif
