using System.Collections.Generic;
using Inkform.Tool;
using Inkform.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Interaction prompt (reusable): while the player overlaps this Interactable's trigger, the
    /// object's silhouette gets an outline glow and a small confirm-key icon floats above it — both
    /// fading in/out smoothly. The icon follows the live input scheme (keyboard keycap / PlayStation
    /// triangle / Xbox Y), so the same prompt works on every supported device without per-device
    /// setups. Purely presentational: it never claims contacts (HandleContact always returns false)
    /// and knows nothing about what the confirm key DOES — pair it with a consumer part (e.g.
    /// AbilityPickupPart) on the same node; both read the same trigger independently.
    ///
    /// Everything visual is code-built at runtime, matching the project's code-built UI convention:
    /// the glow is a second SpriteRenderer (same sprite, Inkform/SpriteOutline material) sorted just
    /// above the original; the icon is a small world-space canvas child anchored to the sprite's top.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPromptPart : MonoBehaviour, IInteractablePart
    {
        [Header("Outline glow")]
        [Tooltip("Material with the Inkform/SpriteOutline shader. Fade is driven per frame by this component; colour and width live in the material.")]
        [SerializeField] private Material outlineMaterial;
        [Tooltip("Glow drawing space around the sprite, as a fraction of the sprite size. A sprite mesh has no pixels outside its silhouette, so the outline quad is scaled up by this and the shader remaps its UVs — raise it together with the material's Outline Width.")]
        [SerializeField, Min(0f)] private float outlinePadding = 0.25f;
        [Tooltip("Breaths per second of the glow while visible; 0 = steady.")]
        [SerializeField, Min(0f)] private float pulseSpeed = 2.2f;
        [Tooltip("How much the breathing dims the glow at its trough, 0..1.")]
        [SerializeField, Range(0f, 1f)] private float pulseStrength = 0.35f;

        [Header("Fade")]
        [Tooltip("Seconds for the glow/icon to fade fully in or out.")]
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.15f;

        [Header("Key icon")]
        [Tooltip("Icon height in world units.")]
        [SerializeField, Min(0.05f)] private float iconWorldSize = 0.5f;
        [Tooltip("Extra local offset from the sprite's top edge (auto-anchored each build).")]
        [SerializeField] private Vector2 promptOffset = new Vector2(0f, 0.18f);

        [Tooltip("Keycap/button background colour (dark, both device families).")]
        [SerializeField] private Color keycapColor = new Color(0.05f, 0.06f, 0.10f, 0.85f);
        [Tooltip("Keyboard letter colour.")]
        [SerializeField] private Color glyphColor = Color.white;
        [Tooltip("PlayStation triangle colour (their north button is a green triangle).")]
        [SerializeField] private Color playStationTint = new Color(0.35f, 0.85f, 0.45f, 1f);
        [Tooltip("Xbox Y letter colour.")]
        [SerializeField] private Color xboxTint = new Color(0.95f, 0.8f, 0.25f, 1f);

        private Interactable root;
        private readonly HashSet<Collider2D> players = new HashSet<Collider2D>();

        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int SpriteRectId = Shader.PropertyToID("_SpriteRect");
        private static readonly int SpriteScaleId = Shader.PropertyToID("_SpriteScale");

        private SpriteRenderer baseRenderer;
        private SpriteRenderer outlineRenderer;
        private Material outlineMaterialInstance;
        private Transform promptRoot;          // the world-space canvas
        private CanvasGroup canvasGroup;
        private RectTransform glyphRect;
        private Vector2 glyphBasePosition;
        private PromptScheme scheme;

        private float fade;

        /// <summary>Whether any player collider currently overlaps this node's trigger. Other parts
        /// may query it, but the pickup part tracks its own set — the two modules stay standalone.</summary>
        public bool PlayerInRange => players.Count > 0;

        public void Attach(Interactable interactable)
        {
            root = interactable;
            baseRenderer = root.GetComponentInChildren<SpriteRenderer>();
            if (baseRenderer == null)
                Debug.LogWarning($"{root.name} interaction prompt found no SpriteRenderer; glow disabled", this);
            if (outlineMaterial == null)
                Debug.LogWarning($"{root.name} interaction prompt has no outline material; glow disabled", this);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (other == null || !other.CompareTag(Tags.Player)) return false;

            if (phase == ContactPhase.Enter) players.Add(other);
            else if (phase == ContactPhase.Exit) players.Remove(other);
            return false;   // presentation only — never claims the contact
        }

        void Update()
        {
            // Destroyed colliders must not keep the prompt stuck open (player GO torn down mid-contact)
            if (players.Count > 0) players.RemoveWhere(collider => collider == null);

            float target = PlayerInRange ? 1f : 0f;
            fade = Mathf.MoveTowards(fade, target, Time.deltaTime / fadeDuration);

            bool visible = fade > 0.001f;
            if (outlineRenderer != null) outlineRenderer.enabled = visible;
            if (promptRoot != null) promptRoot.gameObject.SetActive(visible);
            if (!visible) return;

            EnsureVisualsBuilt();
            if (promptRoot == null) return;

            // Glow breathes around its fade level; the icon fades without breathing (readability)
            float pulse = 1f - pulseStrength * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed));
            if (outlineMaterialInstance != null)
                outlineMaterialInstance.SetFloat(FadeId, fade * pulse);

            canvasGroup.alpha = fade;

            PromptScheme live = InteractPromptIcons.DetectCurrent();
            if (live != scheme) BuildGlyph(live);

            float bob = 4f * Mathf.Sin(Time.time * 3f);   // a few canvas px of float, ~4% of icon size
            if (glyphRect != null) glyphRect.anchoredPosition = glyphBasePosition + Vector2.up * bob;
        }

        private void EnsureVisualsBuilt()
        {
            if (promptRoot != null) return;

            if (baseRenderer != null && outlineMaterial != null)
            {
                var outlineObject = new GameObject("Outline");
                outlineObject.transform.SetParent(root.transform, false);
                outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
                outlineRenderer.sprite = baseRenderer.sprite;
                outlineRenderer.sharedMaterial = outlineMaterial;
                outlineRenderer.sortingLayerID = baseRenderer.sortingLayerID;
                outlineRenderer.sortingOrder = baseRenderer.sortingOrder + 1;

                // A sprite mesh has no pixels outside the silhouette, so the glow quad is scaled up
                // by the padding and the shader remaps its UVs back into sprite space (_SpriteRect /
                // _SpriteScale): the silhouette still renders at its original size and the ring
                // around it becomes drawable band
                float outlineScale = 1f + 2f * outlinePadding;
                outlineRenderer.transform.localScale = Vector3.one * outlineScale;

                // Scaling about the pivot would shove an off-centre pivot's sprite aside — anchor the
                // outline's sprite centre on the base sprite's centre instead (upright, unscaled roots)
                Vector2 baseCenter = (Vector2)baseRenderer.transform.localPosition +
                    (Vector2)baseRenderer.localBounds.center;
                outlineObject.transform.localPosition =
                    baseCenter - outlineScale * (Vector2)outlineRenderer.localBounds.center;

                // The per-renderer copy the fade drives; released in OnDestroy
                outlineMaterialInstance = outlineRenderer.material;
                outlineMaterialInstance.SetVector(SpriteRectId, SpriteUvRect(baseRenderer.sprite));
                outlineMaterialInstance.SetFloat(SpriteScaleId, outlineScale);
                outlineMaterialInstance.SetFloat(FadeId, 0f);
            }

            var canvasObject = new GameObject("Prompt Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(root.transform, false);
            promptRoot = canvasObject.transform;

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (baseRenderer != null)
            {
                canvas.sortingLayerID = baseRenderer.sortingLayerID;
                canvas.sortingOrder = baseRenderer.sortingOrder + 2;
            }
            else
            {
                canvas.sortingOrder = 10;
            }

            canvasGroup = canvasObject.AddComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;

            // Canvas-local pixels → world units: 100 canvas px of children == iconWorldSize
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(100f, 100f);
            canvasRect.localScale = Vector3.one * (iconWorldSize / 100f);

            float top = baseRenderer != null ? baseRenderer.localBounds.extents.y : 0.5f;
            canvasRect.localPosition = new Vector3(promptOffset.x, top + promptOffset.y, 0f);

            var backgroundObject = new GameObject("Keycap", typeof(RectTransform));
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            RectTransform backgroundRect = (RectTransform)backgroundObject.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
            Image background = backgroundObject.AddComponent<Image>();
            background.raycastTarget = false;
            background.color = keycapColor;
            background.sprite = InteractPromptIcons.KeycapSprite;   // replaced per scheme in BuildGlyph

            BuildGlyph(InteractPromptIcons.DetectCurrent());
        }

        /// <summary>Swaps background shape + glyph for the live scheme. Destructive rebuild — it runs
        /// only on scheme change and once at build, never per frame.</summary>
        private void BuildGlyph(PromptScheme live)
        {
            scheme = live;

            Image background = promptRoot.GetComponentInChildren<Image>();
            if (background != null)
            {
                background.sprite = live == PromptScheme.KeyboardMouse
                    ? InteractPromptIcons.KeycapSprite
                    : InteractPromptIcons.ButtonSprite;
            }

            if (glyphRect != null) Destroy(glyphRect.gameObject);

            var glyphObject = new GameObject(live.ToString(), typeof(RectTransform));
            glyphObject.transform.SetParent(promptRoot, false);
            glyphRect = (RectTransform)glyphObject.transform;
            glyphRect.sizeDelta = new Vector2(64f, 64f);
            glyphBasePosition = Vector2.zero;
            glyphRect.anchoredPosition = glyphBasePosition;

            if (live == PromptScheme.PlayStation)
            {
                Image triangle = glyphObject.AddComponent<Image>();
                triangle.sprite = InteractPromptIcons.TriangleSprite;
                triangle.color = playStationTint;
                triangle.raycastTarget = false;
                return;
            }

            Text label = glyphObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 44;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = live == PromptScheme.Xbox ? xboxTint : glyphColor;
            label.text = live == PromptScheme.Xbox
                ? InteractPromptIcons.XboxGlyph
                : InteractPromptIcons.KeyboardGlyph;
            label.raycastTarget = false;
        }

        /// <summary>The sprite's slice rectangle in texture UV space (x0, y0, x1, y1) — the region the
        /// outline shader treats as "the sprite", masking everything outside it.</summary>
        private static Vector4 SpriteUvRect(Sprite sprite)
        {
            Texture2D texture = sprite.texture;
            Rect pixelRect = sprite.textureRect;
            return new Vector4(
                pixelRect.x / texture.width,
                pixelRect.y / texture.height,
                (pixelRect.x + pixelRect.width) / texture.width,
                (pixelRect.y + pixelRect.height) / texture.height);
        }

        void OnDestroy()
        {
            // The per-renderer material copy is not released with the destroyed GameObject — drop it
            // explicitly or it lingers until UnloadUnusedAssets (DestroyImmediate: edit-mode tests)
            if (outlineMaterialInstance == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(outlineMaterialInstance);
            else
#endif
                Destroy(outlineMaterialInstance);
        }
    }
}
