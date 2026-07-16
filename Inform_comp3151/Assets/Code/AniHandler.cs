using Inkform.player;
using UnityEngine;

public class AniHandler : MonoBehaviour
{
    [SerializeField] private Animator animations;
    [SerializeField] private PlayerHandler player;

    void OnEnable()
    {
        player.OnPlayerAction += HandleAction;
    }

    void OnDisable()
    {
        player.OnPlayerAction -= HandleAction;
    }

    private void HandleAction(PlayerState state, FaceDirection face)
    {
        string suffix = face == FaceDirection.R ? "_R" : "_L";
        string clip = state switch
        {
            PlayerState.Idle     => "Idle",
            PlayerState.moving   => "Move",
            // PlayerState.Jump     => "Jump",
            PlayerState.Climbing => "WallClimb_Move",
            PlayerState.Attack   => "Eat",
            _ => "Idle"
        };
        animations.Play(clip + suffix);
    }
    
}
