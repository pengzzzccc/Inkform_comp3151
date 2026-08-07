#if UNITY_EDITOR
using System.Linq;
using UnityEditor.Animations;
using UnityEditor;
using UnityEngine;

namespace Inkform.EditorTools
{
    /// <summary>
    /// Auto-generates/updates every player animation clip from the pre-sliced sprite sheets under
    /// Assets/Animation/Player/Sources and adds them to the Player.controller state machine.
    /// Menu: Tools/Player/Build Animations. Re-run after updating the sources to rebuild (overwrites
    /// frame bindings of same-named clips).
    /// </summary>
    public static class PlayerAnimBuilder
    {
        const string SourceDir = "Assets/Animation/Player/Sources";
        const string OutDir = "Assets/Animation/Player";
        const string ControllerPath = "Assets/Animation/Player/Player.controller";
        const float Fps = 12f;

        struct Entry
        {
            public string sheet;   // source sprite sheet file name (without .png)
            public string clip;    // output clip / state name
            public bool loop;      // whether it loops (one-shots set false)
            public bool flipY;     // whether to flip vertically (reusing Idle frames for ceiling-hanging)
            public Entry(string s, string c, bool l, bool fy = false) { sheet = s; clip = c; loop = l; flipY = fy; }
        }

        // Source sprite sheet -> target clip name / loop. Naming matches the AniHandler mapping.
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
            // Ceiling-still: reuse Idle frames + vertical flip
            new Entry("Idle_L.psd",          "Ceiling_Idle_L",  true, true),
            new Entry("Idle_R.psd",          "Ceiling_Idle_R",  true, true),
        };

        [MenuItem("Tools/Player/Build Animations")]
        public static void Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                Debug.LogWarning($"AnimatorController not found: {ControllerPath} (clips generated, states not added)");

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
                    Debug.LogWarning($"Skipping {e.clip}: {texPath} has no sliced sprites");
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

                if (e.flipY)   // vertical flip: constant m_FlipY = 1 over the whole clip
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
            Debug.Log($"Player animations built: {made}/{Map.Length} clips generated/updated.");
        }

        // Extracts the trailing index from sprite names like "Idle_L.psd_3" / "JumpUp_L_5" to keep frame order
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
                    cs.state.motion = clip;   // same-named state exists -> update only the motion
                    return;
                }
            }
            var ns = sm.AddState(stateName);
            ns.motion = clip;
        }
    }
}
#endif
