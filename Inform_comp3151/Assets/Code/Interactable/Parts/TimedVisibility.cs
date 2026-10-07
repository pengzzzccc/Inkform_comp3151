using Inkform.Audio;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Timed visibility: cycles hazards between "visible onTime → hidden offTime". While hidden the
    /// collider and renderers are fully off and the player passes through; while visible everything
    /// behaves normally. Objects like a timed laser pillar / timed spike attach this component
    /// directly (a laser pillar only needs a collider and a renderer on top).
    ///
    /// Implementation note: the flip only toggles Collider2D and Renderer enabled, **never SetActive** —
    /// deactivation would stop this component's own Update and it could never wake itself when the
    /// hidden phase ends. This is a project-wide rule (BreakablePart.SetBroken / RestorablePart.SetGone
    /// / the former Bomb's SetVisible all toggle enabled). Works on Tilemaps too: TilemapRenderer is a Renderer.
    /// </summary>
    public class TimedVisibility : MonoBehaviour, IInteractablePart
    {
        [Header("Timing")]
        [Tooltip("Visible (collision/rendering on) duration, seconds")]
        [SerializeField] private float onTime = 2f;
        [Tooltip("Hidden (collision/rendering off) duration, seconds. The player can pass through during this window")]
        [SerializeField] private float offTime = 1.5f;
        [Tooltip("Whether it starts visible or hidden")]
        [SerializeField] private bool startVisible = true;

        [Header("Sound")]
        [Tooltip("Looping world sound gated by this hazard's visibility: audible while visible, cut while hidden. " +
            "Assign an AmbientSource (on this object or a child) with its Cue set, and leave that component enabled " +
            "in the prefab — this gate drives its enabled state. Leave empty for a silent hazard")]
        [SerializeField] private AmbientSource loopSource;

        private Collider2D body;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private Timer timer;
        private bool visible;

        public void Attach(Interactable root)
        {
            body = root.GetComponent<Collider2D>();
            if (body == null)
                Debug.LogWarning($"{root.name}'s Interactable has no Collider2D; timed visibility will not work", root);

            // Renderers may sit on the root or children (a Tilemap's rendering is on a child); only
            // toggling enabled, disabled renderers still hand back their references — no activation-order dependency
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));

            visible = startVisible;
            timer.Set(PhaseDuration(visible));
            Apply();
        }

        // Pure driver: consumes no contact, so later parts (like HarmOnTouch) receive it normally
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            if (timer.IsRunning) return;
            Flip();
        }

        private void Flip()
        {
            visible = !visible;
            timer.Set(PhaseDuration(visible));
            Apply();
        }

        // Anti-jitter: a zero duration would flip every frame; clamp to a minimum effective duration
        private float PhaseDuration(bool forOn) => Mathf.Max(forOn ? onTime : offTime, 0.05f);

        private void Apply()
        {
            if (body != null) body.enabled = visible;
            foreach (Renderer r in renderers) r.enabled = visible;

            // The loop follows visibility through AmbientSource's own documented contract: enabling
            // registers a persistent voice, disabling releases it. That keeps AmbientSource the one
            // component that owns looping world audio — no second audio implementation lives here
            if (loopSource != null) loopSource.enabled = visible;
        }

        // A destroyed hazard must not leave its hum behind: the loop is gated by this component, and
        // an AmbientSource sitting on a sibling object would otherwise keep playing
        private void OnDestroy()
        {
            if (loopSource != null) loopSource.enabled = false;
        }
    }
}
