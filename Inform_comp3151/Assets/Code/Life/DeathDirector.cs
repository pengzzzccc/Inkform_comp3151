using Inkform.Bus;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// Death director: hears "someone died", selects the DeathStrategy for the cause, and lets it
    /// perform. The Context of the strategy pattern — this class only selects; how to play is
    /// completely unknown to it.
    ///
    /// Same pattern as FxDirector / AudioDirector (subscribe to buses, centralize configuration in one
    /// Inspector), except those dispatch by concern (screen stuff to the screen, audio to the audio)
    /// while this one dispatches by death cause.
    /// Attach to GameManager.
    /// </summary>
    public class DeathDirector : MonoBehaviour
    {
        [Header("Strategies")]
        [Tooltip("Lookup by death cause. Multiple strategies for the same cause take the first; causes without one fall to the fallback")]
        [SerializeField] private DeathStrategy[] strategies;

        [Tooltip("Fallback when no strategy matches the cause. Empty = that cause plays silently (respawn proceeds normally)")]
        [SerializeField] private DeathStrategy fallback;

        void OnEnable()
        {
            LifeBus.Died += OnDied;
        }

        void OnDisable()
        {
            LifeBus.Died -= OnDied;
        }

        /// <summary>
        /// Strategy for a cause. RespawnDirector also uses it for RespawnDelay — "how quickly you come
        /// back" is a property of the death method, so the lookup logic must exist in exactly one place.
        /// </summary>
        public DeathStrategy Resolve(DeathCause cause)
        {
            if (strategies != null)
            {
                // Linear scan: there are only three causes; a dictionary's cost and mental overhead
                // are not worth it
                foreach (DeathStrategy s in strategies)
                {
                    if (s != null && s.Cause == cause) return s;
                }
            }
            return fallback;
        }

        private void OnDied(DeathContext ctx)
        {
            DeathStrategy strategy = Resolve(ctx.Cause);
            if (strategy == null) return;       // slot unconfigured, skip silently (respawn unaffected)

            if (ctx.Victim == null) return;

            // GetComponent rather than TryGetComponent: the latter is unreliable for interface types.
            // Interfaces are not UnityEngine.Object; when absent it is a true null (no fake-null), so
            // it can be compared directly
            IDeathBody body = ctx.Victim.GetComponent<IDeathBody>();

            // A deceased without IDeathBody is a misconfiguration, not a legal degradation — silently
            // skipping would mean no presentation at all yet a normal respawn, with zero clue for
            // debugging, so complain
            if (body == null)
            {
                Debug.LogWarning($"{ctx.Victim.name} has no IDeathBody; this death will not play out", ctx.Victim);
                return;
            }

            strategy.Execute(ctx, body);
        }
    }
}
