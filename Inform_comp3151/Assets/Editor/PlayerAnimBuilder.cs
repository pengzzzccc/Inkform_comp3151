#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 从 Assets/Animation/Player/Sources 下已切好帧的精灵表，自动生成/更新所有玩家动画 clip，
/// 并把它们加入 Player.controller 的状态机。菜单：Tools/Player/Build Animations。
/// 源文件更新后重新点一次即可重建（会覆盖同名 clip 的帧绑定）。
/// </summary>
public static class PlayerAnimBuilder
{
    const string SourceDir = "Assets/Animation/Player/Sources";
    const string OutDir = "Assets/Animation/Player";
    const string ControllerPath = "Assets/Animation/Player.controller";
    const float Fps = 12f;

    struct Entry
    {
        public string sheet;   // 源精灵表文件名（不含 .png）
        public string clip;    // 输出 clip / 状态名
        public bool loop;      // 是否循环（一次性动作设 false）
        public bool flipY;     // 是否上下翻转（复用 Idle 帧做天花板倒吊）
        public Entry(string s, string c, bool l, bool fy = false) { sheet = s; clip = c; loop = l; flipY = fy; }
    }

    // 源精灵表 -> 目标 clip 名 / 循环。命名与 AniHandler 的映射一致。
    static readonly Entry[] Map =
    {
        new Entry("Idle_L.psd",          "Idle_L",          true),
        new Entry("Idle_R.psd",          "Idle_R",          true),
        new Entry("Move_L",              "Move_L",          true),
        new Entry("Move_R",              "Move_R",          true),
        new Entry("JumpUp_L",            "JumpUp_L",        false),
        new Entry("JumpUp_R",            "JumpUp_R",        false),
        new Entry("RiseUp",              "RiseUp",          true),
        new Entry("FallDown_L",          "FallDown_L",      true),
        new Entry("FallDown_R",          "FallDown_R",      true),
        new Entry("Land",                "Land",            false),
        new Entry("Eat_L",               "Eat_L",           false),
        new Entry("Eat_R",               "Eat_R",           false),
        new Entry("Release_L",           "Release_L",       false),
        new Entry("Release_R",           "Release_R",       false),
        new Entry("StickCeiling_FaceL",  "Ceiling_Stick_L", true),
        new Entry("StickCeiling_FaceR",  "Ceiling_Stick_R", true),
        new Entry("WallClimb_MoveL",     "Ceiling_Move_L",  true),
        new Entry("WallClimb_MoveR",     "Ceiling_Move_R",  true),
        new Entry("WallClimb_LSide",     "Wall_Slide_L",    true),
        new Entry("WallClimb_RSide",     "Wall_Slide_R",    true),
        // 天花板静止：复用 Idle 帧 + 上下翻转
        new Entry("Idle_L.psd",          "Ceiling_Idle_L",  true, true),
        new Entry("Idle_R.psd",          "Ceiling_Idle_R",  true, true),
    };

    [MenuItem("Tools/Player/Build Animations")]
    public static void Build()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            Debug.LogWarning($"找不到 AnimatorController: {ControllerPath}（只生成 clip，不加状态）");

        int made = 0;
        foreach (var e in Map)
        {
            string texPath = $"{SourceDir}/{e.sheet}.png";
            var sprites = AssetDatabase.LoadAllAssetsAtPath(texPath)
                .OfType<Sprite>()
                .OrderBy(s => ExtractIndex(s.name))
                .ToArray();

            if (sprites.Length == 0)
            {
                Debug.LogWarning($"跳过 {e.clip}：{texPath} 没有已切片的精灵");
                continue;
            }

            var clip = new AnimationClip { frameRate = Fps };
            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite"
            };
            var keys = new ObjectReferenceKeyframe[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = i / Fps, value = sprites[i] };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = e.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (e.flipY)   // 上下翻转：整段 m_FlipY 常量=1
                clip.SetCurve("", typeof(SpriteRenderer), "m_FlipY",
                    AnimationCurve.Constant(0f, sprites.Length / Fps, 1f));

            string outPath = $"{OutDir}/{e.clip}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                clip = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(clip, outPath);
            }

            if (controller != null) EnsureState(controller, e.clip, clip);
            made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Player animations built: {made}/{Map.Length} 个 clip 已生成/更新。");
    }

    // 从 "Idle_L.psd_3" / "JumpUp_L_5" 这样的精灵名里取末尾序号，保证帧顺序
    static int ExtractIndex(string spriteName)
    {
        int u = spriteName.LastIndexOf('_');
        if (u >= 0 && int.TryParse(spriteName.Substring(u + 1), out int idx)) return idx;
        return 0;
    }

    static void EnsureState(AnimatorController controller, string stateName, AnimationClip clip)
    {
        var sm = controller.layers[0].stateMachine;
        foreach (var cs in sm.states)
        {
            if (cs.state.name == stateName)
            {
                cs.state.motion = clip;   // 已存在同名状态 -> 只更新 motion
                return;
            }
        }
        var ns = sm.AddState(stateName);
        ns.motion = clip;
    }
}
#endif
