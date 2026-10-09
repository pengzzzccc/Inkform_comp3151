using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// Frame step for presentation animations (scene curtain, intro hold, cinematic bars).
    /// Time.unscaledDeltaTime is not capped by Time.maximumDeltaTime, so the hitch frames right
    /// after a scene load (hundreds of ms each in a large room) would swallow a whole sub-second
    /// animation in one step. Capping the step makes a hitch pause the animation instead of
    /// skipping it. Presentation only — gameplay timers keep using Timer / Time.time.
    /// </summary>
    public static class PresentationTime
    {
        public const float MaxStep = 1f / 30f;

        public static float UnscaledStep => Mathf.Min(Time.unscaledDeltaTime, MaxStep);
    }
}
