using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// Shatter death: the body vanishes on the spot, bursting into shards, with a full screen-feedback
    /// set and one death sound. Spike deaths use this one; with a different FragmentCue and heavier
    /// numbers, the same class can perform "killed by an explosion" too.
    ///
    /// Handling shards, screen feedback, and sound all here is deliberate: one death method's whole
    /// configuration sits in one asset — tuning "how much a spike death hurts" never bounces between
    /// three Inspectors (FxDirector, AudioDirector, PlayerDeathFx). The cost is that presentation
    /// logic switches from "split by concern" to "split by death cause" — FxDirector / AudioDirector
    /// still handle every other event (explosions, wall breaks, eat/spit, jump/land), they just no
    /// longer manage death.
    /// </summary>
    [CreateAssetMenu(menuName = "Life/Shatter Death Strategy")]
    public class ShatterDeathStrategy : DeathStrategy
    {
        [Header("Death burst")]
        [SerializeField] private FragmentCue pieces;        // what it shatters into is all written in this asset
        [SerializeField] private float burstForce = 14f;    // shard initial velocity, then × the Cue's forceMultiplier

        // Death is the strongest feedback in the game; every item should outweigh an explosion.
        // No distance falloff here: death always happens on the player ≈ camera center, the computed
        // factor would be 1
        [Header("Screen FX")]
        [SerializeField] private float trauma = 0.7f;
        [SerializeField] private float hitStop = 0.12f;     // ScreenFx.maxHitStop is 0.25, do not exceed
        [SerializeField] private float zoom = -0.5f;        // negative = push in
        [SerializeField] private float zoomTime = 0.4f;
        [SerializeField] private float punch = 0.8f;        // vignette strength increment; only death uses post-processing at all
        [SerializeField] private float punchTime = 0.5f;

        [Header("Audio")]
        [SerializeField] private SoundCue deathCue;

        public override void Execute(in DeathContext ctx, IDeathBody body)
        {
            // Order matters: bounds must be captured before Hide(), once rendering is off bounds
            // degenerate to zero size at the origin and every shard piles up at the world origin
            Bounds bounds = body.VisualBounds;
            body.Hide();

            // Shatter itself skips silently when the Cue is unconfigured; no second check needed here
            Shatter.Burst(pieces, bounds, ctx.From, burstForce);

            // Each item can be zeroed out separately in the Inspector without affecting the others
            // (same style as FxDirector)
            if (trauma > 0f) FxBus.RaiseShake(trauma);
            if (hitStop > 0f) FxBus.RaiseHitStop(hitStop);
            if (zoom != 0f) FxBus.RaiseZoom(zoom, zoomTime);
            if (punch > 0f) FxBus.RaisePunch(punch, punchTime);

            // No position passed: death always happens on the player ≈ camera center, the attenuation
            // factor is necessarily near 1. Empty slots or no AudioManager in the scene both skip silently
            if (deathCue != null && AudioManager.Instance != null)
                AudioManager.Instance.Play(deathCue);
        }
    }
}
