using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// Burn death: the body does not burst and does not hide — its material is swapped for the
    /// burn-dissolve shader and it burns away in place, the front spreading from the contact
    /// point across the whole silhouette. FireJet deaths use this one.
    ///
    /// The burn front needs a true contact point: ctx.From is the hazard's closest point to the
    /// player, so touching a laser with the lower-left corner really does ignite the lower left.
    /// All fire visuals (flame band, embers, scorch) are tuned on the material asset; this class
    /// only carries the pace and the screen/audio feedback.
    /// </summary>
    [CreateAssetMenu(menuName = "Life/Burn Death Strategy")]
    public class BurnDeathStrategy : DeathStrategy
    {
        [Header("Burn")]
        [Tooltip("Material using Inkform/BurnDissolve — flame colors, raggedness and ember trail are tuned on it")]
        [SerializeField] private Material burnMaterial;
        [SerializeField] private float burnDuration = 0.7f;  // front travel time, contact point → farthest corner
        [Tooltip("Ash bit size, in grain chunks — 1 = one shader pixel block, smaller = finer ash")]
        [SerializeField] private float ashSize = 0.6f;

        // Death is the strongest feedback in the game; every item should outweigh an explosion.
        // Each item can be zeroed out separately in the Inspector without affecting the others
        [Header("Screen FX")]
        [SerializeField] private float trauma = 0.5f;
        [SerializeField] private float hitStop = 0.1f;      // ScreenFx.maxHitStop is 0.25, do not exceed
        [SerializeField] private float zoom = -0.4f;        // negative = push in
        [SerializeField] private float zoomTime = 0.45f;
        [SerializeField] private float punch = 0.9f;        // vignette strength increment
        [SerializeField] private float punchTime = 0.6f;

        [Header("Audio")]
        [SerializeField] private SoundCue deathCue;

        public override void Execute(in DeathContext ctx, IDeathBody body)
        {
            // No Hide(), no shards: the shader dissolves the visible body in place and BurnAwayFx
            // restores the original material when it finishes (or on respawn, whichever is first)
            SpriteRenderer sprite = ctx.Victim.GetComponent<SpriteRenderer>();
            BurnAwayFx.Spawn(sprite, burnMaterial, ctx.From, burnDuration, ashSize);

            if (trauma > 0f) FxBus.RaiseShake(trauma);
            if (hitStop > 0f) FxBus.RaiseHitStop(hitStop);
            if (zoom != 0f) FxBus.RaiseZoom(zoom, zoomTime);
            if (punch > 0f) FxBus.RaisePunch(punch, punchTime);

            // No position passed: death always happens on the player ≈ camera center. Empty slots
            // or no AudioManager in the scene both skip silently
            if (deathCue != null && AudioManager.Instance != null)
                AudioManager.Instance.Play(deathCue);
        }
    }
}
