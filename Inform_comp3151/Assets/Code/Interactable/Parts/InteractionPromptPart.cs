using System.Collections.Generic;
using Inkform.Fx;
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
        [Tooltip("Material with the Inkform/SpriteOutline shader. Fade and width are driven per renderer; colour and intensity live in the material.")]
        [SerializeField] private Material outlineMaterial;
        [Tooltip("Outline thickness in local world units. It is converted to source texels so sprites with different Pixels Per Unit have the same visible thickness.")]
        [SerializeField, Min(0f)] private float outlineWorldWidth = 0.0625f;
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


        private SpriteRenderer baseRenderer;
        private SpriteRenderer outlineRenderer;
        private Material outlineMaterialInstance;
        private Transform promptRoot;          // the world-space canvas
        private CanvasGroup canvasGroup;
        private RectTransform glyphRect;
        private Vector2 glyphBasePosition;
        private PromptScheme scheme;

        private float fade;
        private bool suppressed;

        /// <summary>Whether any player collider currently overlaps this node's trigger. Other parts
        /// may query it, but the pickup part tracks its own set — the two modules stay standalone.</summary>
        public bool PlayerInRange => players.Count > 0;
        public bool IsSuppressed => suppressed;

        public void Attach(Interactable interactable)
        {
            root = interactable;
            baseRenderer = root.GetComponentInChildren<SpriteRenderer>();
            if (baseRenderer == null)
                Debug.LogWarning($"{root.name} interaction prompt found no SpriteRenderer; glow disabled", this);
            else if (baseRenderer.sprite == null)
                Debug.LogWarning($"{root.name} interaction prompt has no sprite on its SpriteRenderer; the glow and icon stay hidden until one is assigned", this);
            if (outlineMaterial == null)
                Debug.LogWarning($"{root.name} interaction prompt has no outline material; glow disabled", this);
        }

        void Start()
        {
            // Build before the first proximity frame. Creating a SpriteRenderer lazily in Update can
            // leave its initial 2D-renderer bounds/material state stale until a Transform changes.
            if (baseRenderer == null || baseRenderer.sprite == null) return;
            EnsureVisualsBuilt();
            if (outlineRenderer != null) outlineRenderer.enabled = false;
            if (promptRoot != null) promptRoot.gameObject.SetActive(false);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (other == null || !other.CompareTag(Tags.Player)) return false;

            if (phase == ContactPhase.Enter) players.Add(other);
            else if (phase == ContactPhase.Exit) players.Remove(other);
            return false;   // presentation only — never claims the contact
        }

        /// <summary>Temporarily hides this prompt while another presentation owns the object.</summary>
        public void SetSuppressed(bool value)
        {
            if (suppressed == value) return;
            suppressed = value;
            if (!suppressed) return;

            // Map inspection freezes scaled time, so waiting for the normal fade would leave the
            // key icon over the focused artwork forever. Suppression is intentionally immediate.
            fade = 0f;
            if (outlineRenderer != null) outlineRenderer.enabled = false;
            if (promptRoot != null) promptRoot.gameObject.SetActive(false);
        }

        void Update()
        {
            // Destroyed colliders must not keep the prompt stuck open (player GO torn down mid-contact)
            if (players.Count > 0) players.RemoveWhere(collider => collider == null);

            if (suppressed)
            {
                if (outlineRenderer != null) outlineRenderer.enabled = false;
                if (promptRoot != null) promptRoot.gameObject.SetActive(false);
                return;
            }

            float target = PlayerInRange ? 1f : 0f;
            fade = Mathf.MoveTowards(fade, target, Time.deltaTime / fadeDuration);

            // No sprite means nothing to outline and nowhere to anchor the icon — stay hidden
            // (checked per frame, so assigning the sprite later just starts the prompt working)
            bool hasArt = baseRenderer != null && baseRenderer.sprite != null;
            bool visible = fade > 0.001f && hasArt;
            if (outlineRenderer != null) outlineRenderer.enabled = visible;
            if (promptRoot != null) promptRoot.gameObject.SetActive(visible);
            if (!visible) return;

            EnsureVisualsBuilt();
            if (promptRoot == null) return;

            // Glow breathes around its fade level; the icon fades without breathing (readability)
            float pulse = 1f - pulseStrength * 0.5f * (1f + Mathf.Sin(Time.time * pulseSpeed));
            SpriteGlow.SetFade(outlineMaterialInstance, fade * pulse);

            canvasGroup.alpha = fade;

            PromptScheme live = InteractPromptIcons.DetectCurrent();
            if (live != scheme) BuildGlyph(live);

            float bob = 4f * Mathf.Sin(Time.time * 3f);   // a few canvas px of float, ~4% of icon size
            if (glyphRect != null) glyphRect.anchoredPosition = glyphBasePosition + Vector2.up * bob;
        }

        private void EnsureVisualsBuilt()
        {
            if (promptRoot != null) return;

            // The glow is prebuilt disabled at fade 0; contact enables it and the update drives the fade
            outlineRenderer = SpriteGlow.Create(baseRenderer, outlineMaterial, outlinePadding,
                outlineWorldWidth, "Outline", out outlineMaterialInstance);

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
            label.font = UiFonts.Primary;
            label.fontSize = 44;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = live == PromptScheme.Xbox ? xboxTint : glyphColor;
            label.text = live == PromptScheme.Xbox
                ? InteractPromptIcons.XboxGlyph
                : InteractPromptIcons.KeyboardGlyph;
            label.raycastTarget = false;
        }

        void OnDestroy() => SpriteGlow.Release(outlineMaterialInstance);

        // ---- Scene-view gizmo ----

        // Shows where the prompt icon will float, using the same placement formula as
        // EnsureVisualsBuilt (root-local: x = offset.x, y = sprite top + offset.y) — resolved the
        // same way for the edit mode, where Attach has not run yet
        private void OnDrawGizmosSelected()
        {
            SpriteRenderer spriteRenderer = baseRenderer != null ? baseRenderer : GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null) return;

            Interactable node = root != null ? root : GetComponentInParent<Interactable>();
            Transform parent = node != null ? node.transform : transform;

            Bounds bounds = spriteRenderer.localBounds;
            float top = spriteRenderer.sprite != null ? bounds.extents.y : 0.5f;   // matches the runtime no-sprite fallback

            Vector3 promptWorld = parent.TransformPoint(new Vector3(promptOffset.x, top + promptOffset.y, 0f));
            Vector3 spriteWorld = parent.TransformPoint(spriteRenderer.transform.localPosition + (Vector3)bounds.center);

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);   // the same yellow as the parts overview
            Gizmos.DrawLine(spriteWorld, promptWorld);
            Gizmos.DrawWireSphere(promptWorld, iconWorldSize * 0.5f);   // the icon's approximate footprint
        }
    }
}
