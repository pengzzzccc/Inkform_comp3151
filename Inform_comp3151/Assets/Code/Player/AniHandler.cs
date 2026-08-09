using Inkform.Bus;
using UnityEngine;

namespace Inkform.Player
{
    public class AniHandler : MonoBehaviour
    {
        [SerializeField] private Animator animations;
        [SerializeField] private SpriteRenderer sprite;

        void Awake()
        {
            // The clip curves use an empty path, so the SpriteRenderer must sit on the same object as
            // the Animator
            if (sprite == null && animations != null) sprite = animations.GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            PlayerBus.StateChanged += OnState;
            PlayerBus.FaceChanged += OnFace;
            Refresh();                       // initial sync from the snapshot
        }

        void OnDisable()
        {
            PlayerBus.StateChanged -= OnState;
            PlayerBus.FaceChanged -= OnFace;
        }

        private void OnState(PlayerState state) => Refresh();
        private void OnFace(FaceDirection face) => Refresh();

        private void Refresh()
        {
            if (animations == null) return;   // HasState needs it; guard first

            bool faceL = PlayerBus.Face == FaceDirection.L;

            // selfDirectional: the state name carries its direction (which wall the player faces was
            // already chosen by PlayerHandler), no suffix or flip
            (string baseName, bool selfDirectional) = PlayerBus.State switch
            {
                PlayerState.Idle         => ("Idle",          false),
                PlayerState.Move         => ("Move",          false),
                PlayerState.JumpUp       => ("JumpUp",        false),
                PlayerState.Rise         => ("RiseUp",        false),
                PlayerState.Fall         => ("FallDown",      false),
                PlayerState.Land         => ("Land",          false),
                PlayerState.CeilingStick => ("Ceiling_Stick", false),
                PlayerState.CeilingMove  => ("Ceiling_Move",  false),
                PlayerState.CeilingIdle  => ("Ceiling_Idle",  false),
                PlayerState.WallSlideL   => ("Wall_Slide_L",  true),
                PlayerState.WallSlideR   => ("Wall_Slide_R",  true),
                _                        => ("Idle",          false),
            };

            if (selfDirectional) { Play(baseName, false); return; }

            string directional = baseName + (faceL ? "_L" : "_R");
            if (HasState(directional)) Play(directional, false);  // _L/_R mirrored art exists: facing is drawn in the frames
            else                       Play(baseName, faceL);     // only right-facing art (RiseUp / Land): flipX supplies left
        }

        // flip and clip must apply on the same frame, or a wrong facing flashes for one frame.
        // Do not call Play on a missing state, to avoid the Animator printing "state does not exist"
        private void Play(string stateName, bool flip)
        {
            if (sprite != null) sprite.flipX = flip;
            if (HasState(stateName)) animations.Play(stateName);
        }

        private bool HasState(string stateName)
            => animations.HasState(0, Animator.StringToHash(stateName));

    }
}
