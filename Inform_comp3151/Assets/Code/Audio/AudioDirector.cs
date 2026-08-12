using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Player;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Audio director: translates "what happened in the game" into "which sound to play".
    /// Same pattern as FxDirector — subscribes to buses, centralizes which SoundCue each event maps to
    /// in this one Inspector; Bomb / PlayerHandler / BreakablePart never need to know the audio system exists.
    /// Leaving a slot empty is legal: AudioManager skips it silently; drop a Cue into the slot later and
    /// it sounds without touching code.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        [Header("Hazard")]
        [SerializeField] private SoundCue blast;        // explosion (exactly once per explosion)
        [SerializeField] private SoundCue wallBreak;    // breakable wall shatter
        [SerializeField] private SoundCue bombTick;     // bomb warning frame advance (proximity warning + fuse countdown share it)

        [Header("Item")]
        [SerializeField] private SoundCue itemEaten;    // swallow
        [SerializeField] private SoundCue itemSpit;     // spit out

        [Header("Player")]
        [SerializeField] private SoundCue jump;
        [SerializeField] private SoundCue land;

        // Death sounds are not here: they dispatch by cause rather than by concern (spiked vs fallen
        // should sound different), so the Cue lives on the DeathStrategy asset and is played by the strategy.
        [Header("Life")]
        [SerializeField] private SoundCue respawn;      // respawn at checkpoint
        [SerializeField] private SoundCue checkpoint;   // stepped on checkpoint

        void OnEnable()
        {
            HazardBus.Blast += OnBlast;
            HazardBus.Broken += OnBroken;
            HazardBus.Ticked += OnTicked;
            ItemBus.ItemEaten += OnItemEaten;
            ItemBus.ItemReleased += OnItemReleased;
            PlayerBus.StateChanged += OnPlayerState;
            LifeBus.Respawned += OnRespawned;
            LifeBus.CheckpointSet += OnCheckpointSet;
        }

        void OnDisable()
        {
            HazardBus.Blast -= OnBlast;
            HazardBus.Broken -= OnBroken;
            HazardBus.Ticked -= OnTicked;
            ItemBus.ItemEaten -= OnItemEaten;
            ItemBus.ItemReleased -= OnItemReleased;
            PlayerBus.StateChanged -= OnPlayerState;
            LifeBus.Respawned -= OnRespawned;
            LifeBus.CheckpointSet -= OnCheckpointSet;
        }

        // Explosions must only listen to Blast — Exploded fires per victim in a foreach, N victims = N sounds
        private void OnBlast(Vector2 center, float radius, float force) => Play(blast, center);

        private void OnBroken(Vector2 center) => Play(wallBreak, center);

        // Proximity warning and fuse countdown share this one sound. step / total are unused for now,
        // kept so "higher pitch as it gets closer" needs no bus signature change later
        private void OnTicked(Vector2 pos, int step, int total) => Play(bombTick, pos);

        private void OnItemEaten(ICarriable item) => Play(itemEaten);

        // Both of these are "the player's own sounds", always at the camera center, so like
        // attack/jump/land they pass no position. The matching Cue assets should keep spatial off —
        // see the Tooltip in SoundCue.cs
        private void OnRespawned(GameObject victim, Vector2 pos) => Play(respawn);

        private void OnCheckpointSet(Vector2 pos) => Play(checkpoint);

        // The spit origin is always on the player ≈ camera center, so the attenuation factor is
        // necessarily near 1 — passing the position is only for "pass position when we have one"
        // consistency; audibly identical to passing nothing
        private void OnItemReleased(ICarriable item, Vector2 pos, Vector2 velocity) => Play(itemSpit, pos);

        // PlayerBus already dedups (broadcasts only when a state actually changes), so no per-frame spam.
        // Eat/Release animations were removed (dash is pure dash), leaving only jump and land
        private void OnPlayerState(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.JumpUp: Play(jump); break;
                case PlayerState.Land: Play(land); break;
            }
        }

        // Position only says "where the sound happens"; whether and how attenuation applies is decided
        // by SoundCue.spatial — Cues without it are unaffected by passing one.
        // Note the underlying playback is always 2D: this is a 2D game — switching to Unity's 3D audio
        // would use its default logarithmic falloff (minDistance 1), and since the camera sits at z = -10,
        // its computed distance is always ≥10, crushing explosions to near inaudible. AudioManager
        // computes distance on the XY plane itself, bypassing the issue.
        private void Play(SoundCue cue, Vector3? position = null)
        {
            if (cue == null) return;                        // slot unconfigured, skip silently
            if (AudioManager.Instance == null) return;      // no AudioManager in the scene yet
            AudioManager.Instance.Play(cue, position);
        }
    }
}
