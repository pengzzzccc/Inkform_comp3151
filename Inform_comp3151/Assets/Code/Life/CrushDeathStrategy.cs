using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// Crush death: the body does not burst and does not hide — it squashes into a flat pancake
    /// where it stood and stays there until respawn (PlayerDeathFx restores the scale, so nothing
    /// leaks into the next life). Crusher deaths use this one.
    ///
    /// Screen feedback is the heaviest of all deaths (being flattened should feel like it); what
    /// sells the death is the flatten itself playing through the hitStop, which is why Flatten
    /// animates on unscaled time.
    /// </summary>
    [CreateAssetMenu(menuName = "Life/Crush Death Strategy")]
    public class CrushDeathStrategy : DeathStrategy
    {
        [Header("Flatten")]
        [SerializeField] private Vector2 squash = new Vector2(1.6f, 0.12f);  // x widens, y flattens
        [SerializeField] private float flattenTime = 0.1f;

        // Death is the strongest feedback in the game; every item should outweigh an explosion.
        // Each item can be zeroed out separately in the Inspector without affecting the others
        [Header("Screen FX")]
        [SerializeField] private float trauma = 0.9f;
        [SerializeField] private float hitStop = 0.18f;     // ScreenFx.maxHitStop is 0.25, do not exceed
        [SerializeField] private float zoom = -0.6f;        // negative = push in
        [SerializeField] private float zoomTime = 0.4f;
        [SerializeField] private float punch = 0.85f;       // vignette strength increment
        [SerializeField] private float punchTime = 0.5f;

        [Header("Audio")]
        [SerializeField] private SoundCue deathCue;

        public override void Execute(in DeathContext ctx, IDeathBody body)
        {
            body.Flatten(squash, flattenTime);

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
