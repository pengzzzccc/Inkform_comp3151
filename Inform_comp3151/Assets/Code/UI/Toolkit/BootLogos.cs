using Inkform.Audio;
using UnityEngine;

namespace Inkform.UI
{
    /// <summary>
    /// Boot sequence content: the studio card, the game card and the sting that plays as the game
    /// card fades in. Every slot is deliberately optional — an empty sprite degrades to a text
    /// placeholder ("STUDIO LOGO" / gameTitle) and an empty sting stays silent, so the whole
    /// sequence runs before any art exists. Drop real assets into Resources/UI/BootLogos.asset
    /// and the code never changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Inkform/Boot Logos", fileName = "BootLogos")]
    public class BootLogos : ScriptableObject
    {
        [Tooltip("Studio card art. Empty = text placeholder.")]
        public Sprite studioLogo;

        [Tooltip("Game card art. Empty = text placeholder built from gameTitle.")]
        public Sprite gameLogo;

        [Tooltip("One-shot sting played as the game card starts fading in. Empty = silent.")]
        public SoundCue sting;

        [Tooltip("Placeholder / fallback title text for the game card.")]
        public string gameTitle = "BOMB SLIME";

        [Tooltip("Studio card: full-visible dwell before the fade-out, in seconds.")]
        public float studioHoldSeconds = 2f;

        [Tooltip("Fade in/out length of each card, in seconds.")]
        public float fadeSeconds = 0.4f;
    }
}
