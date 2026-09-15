using System.IO;
using System.Linq;
using Inkform.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.EditorTools
{
    /// <summary>
    /// One-time asset bootstrap for the UI Toolkit migration — the editor-side duties the old
    /// UIBuilder used to carry, trimmed to what the Toolkit UI still needs:
    ///
    ///   - MainPanel.asset: the PanelSettings the runtime UIDocument loads (1920x1080
    ///     scale-with-screen-size, match 0.5 — the old CanvasScaler's policy — pointing at the
    ///     RuntimeTheme.tss). Plain .asset because Unity 6 refuses CreateAsset for .panelsettings files;
    ///     a .panelsettings left by an earlier run is migrated (moved) automatically. The
    ///     UIManager configures an identical runtime instance when this asset is missing, so the
    ///     game still runs without it, but the asset makes the setup inspectable.
    ///   - TutorialPages.asset: the tutorial's page sprites (pre-filled from Art/UI/Tutirial),
    ///     which used to live in serialized arrays on the Tutorial prefab.
    ///   - Resources copies of the fonts (BombSlimeFonts + LiberationSans), so the runtime
    ///     document can load them (Resources.Load cannot reach Art/UI or TextMesh Pro/Fonts).
    ///
    /// Everything is idempotent and runs automatically once per editor session (delayCall, fully
    /// guarded — this must never break import). The menu item exists for manual re-runs.
    /// </summary>
    public static class UIToolkitBootstrap
    {
        private const string ResDir = "Assets/Resources/UI";
        // Plain .asset: Unity 6 refuses AssetDatabase.CreateAsset for .panelsettings files
        // ("change the file type to '*.asset'"). Resources.Load resolves by name and type, not
        // extension, so the runtime load path is unaffected.
        private const string PanelPath = ResDir + "/MainPanel.asset";
        private const string PanelPathLegacy = ResDir + "/MainPanel.panelsettings";
        // RuntimeTheme, not Theme: the .tss must not share Resources/UI with Theme.uss.
        // Resources.Load<StyleSheet>("UI/Theme") cannot tell a ThemeStyleSheet from its USS base
        // type, so a same-named pair resolves ambiguously at runtime.
        private const string ThemePath = ResDir + "/RuntimeTheme.tss";
        private const string ThemePathLegacy = ResDir + "/Theme.tss";
        private const string TutorialPath = ResDir + "/TutorialPages.asset";
        private const string TutorialArtDir = "Assets/Art/UI/Tutirial";

        /// <summary>Fonts copied into Resources so Resources.Load can reach them (it cannot load
        /// from Art/ or TextMesh Pro/). BombSlime is the project pixel font; LiberationSans is the
        /// clean control face for the UIManager's font experiment.</summary>
        private static readonly (string source, string target)[] FontCopies =
        {
            ("Assets/Art/UI/BombSlimeFonts.ttf", ResDir + "/BombSlimeFonts.ttf"),
            ("Assets/TextMesh Pro/Fonts/LiberationSans.ttf", ResDir + "/LiberationSans.ttf"),
        };

        [InitializeOnLoadMethod]
        private static void AutoRun()
        {
            // After the import settles; swallow everything — an asset database hiccup here must
            // never surface as an editor error dialog on project load.
            EditorApplication.delayCall += () => { try { Run(); } catch { } };
        }

        [MenuItem("Tools/Inkform/UIToolkit Bootstrap")]
        public static void Run()
        {
            Directory.CreateDirectory(ResDir);

            // Font copy for Resources.Load<Font> (the runtime document inherits it from the root).
            foreach ((string fontSource, string fontTarget) in FontCopies)
            {
                if (!File.Exists(fontTarget) && File.Exists(fontSource))
                    File.Copy(fontSource, fontTarget);
                if (File.Exists(fontTarget) && AssetDatabase.LoadAssetAtPath<Font>(fontTarget) == null)
                    AssetDatabase.ImportAsset(fontTarget);
            }

            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (settings == null && File.Exists(PanelPathLegacy))
            {
                // First run after the rename: move the previously generated .panelsettings over
                // instead of leaving two assets for the same thing behind.
                string moveError = AssetDatabase.MoveAsset(PanelPathLegacy, PanelPath);
                if (!string.IsNullOrEmpty(moveError))
                    Debug.LogWarning($"UIToolkitBootstrap: could not migrate {PanelPathLegacy} — {moveError}");
                settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            }
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelPath);
            }

            // The asset's one policy, re-asserted on every run — a run from an older script state
            // once wrote ConstantPhysicalSize here, which rendered the UI unscaled in a corner of
            // larger windows. SetDirty only when something actually changed.
            bool dirty = false;
            if (settings.scaleMode != PanelScaleMode.ScaleWithScreenSize)
            {
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                dirty = true;
            }
            if (settings.referenceResolution != new Vector2Int(1920, 1080))
            {
                settings.referenceResolution = new Vector2Int(1920, 1080);
                dirty = true;
            }
            if (settings.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight)
            {
                settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                dirty = true;
            }
            if (!Mathf.Approximately(settings.match, 0.5f))
            {
                settings.match = 0.5f;
                dirty = true;
            }
            if (settings.sortingOrder != 100)
            {
                settings.sortingOrder = 100;
                dirty = true;
            }
            if (settings.themeStyleSheet == null)
            {
                // First run after the rename: move the previously generated Theme.tss over
                // instead of leaving a name-colliding pair behind.
                if (AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath) == null && File.Exists(ThemePathLegacy))
                {
                    string themeMoveError = AssetDatabase.MoveAsset(ThemePathLegacy, ThemePath);
                    if (!string.IsNullOrEmpty(themeMoveError))
                        Debug.LogWarning($"UIToolkitBootstrap: could not migrate {ThemePathLegacy} — {themeMoveError}");
                }

                ThemeStyleSheet theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
                if (theme != null)
                {
                    settings.themeStyleSheet = theme;
                    dirty = true;
                }
            }
            if (dirty)
            {
                EditorUtility.SetDirty(settings);
                Debug.LogWarning("UIToolkitBootstrap: MainPanel panel settings were corrected (scale mode must be Scale With Screen Size @ 1920x1080, match 0.5)", settings);
            }

            if (AssetDatabase.LoadAssetAtPath<TutorialPages>(TutorialPath) == null)
            {
                TutorialPages pages = ScriptableObject.CreateInstance<TutorialPages>();
                pages.checkpointPages = LoadPageSprites("TimeCardT");
                pages.ropeGunPages = LoadPageSprites("RopeGunT");
                AssetDatabase.CreateAsset(pages, TutorialPath);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>The tutorial page screenshots, ordered by their trailing page number (same
        /// folder the old UIBuilder filled the prefab arrays from).</summary>
        private static Sprite[] LoadPageSprites(string prefix)
        {
            if (!Directory.Exists(TutorialArtDir)) return System.Array.Empty<Sprite>();

            return Directory.GetFiles(TutorialArtDir, prefix + "*.png")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => TrailingNumber(name) ?? int.MaxValue)
                .Select(name => AssetDatabase.LoadAssetAtPath<Sprite>($"{TutorialArtDir}/{name}.png"))
                .Where(sprite => sprite != null)
                .ToArray();
        }

        private static int? TrailingNumber(string name)
        {
            int digits = 0;
            int value = 0;
            int scale = 1;
            for (int i = name.Length - 1; i >= 0 && char.IsDigit(name[i]); i--)
            {
                value += (name[i] - '0') * scale;
                scale *= 10;
                digits++;
            }
            return digits > 0 ? value : (int?)null;
        }
    }
}
