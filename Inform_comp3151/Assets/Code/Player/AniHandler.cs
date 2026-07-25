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
        string baseName = state switch
        {
            PlayerState.Idle         => "Idle",
            PlayerState.Move         => "Move",
            PlayerState.JumpUp       => "JumpUp",
            PlayerState.Rise         => "RiseUp",
            PlayerState.Fall         => "FallDown",
            PlayerState.Land         => "Land",
            PlayerState.Eat          => "Eat",
            PlayerState.Release      => "Release",
            PlayerState.CeilingStick => "Ceiling_Stick",
            PlayerState.CeilingMove  => "Ceiling_Move",
            PlayerState.CeilingIdle  => "Ceiling_Idle",
            PlayerState.WallSlideL   => "Wall_Slide_L",
            PlayerState.WallSlideR   => "Wall_Slide_R",
            _                        => "Idle"
        };

        string suffix = face == FaceDirection.R ? "_R" : "_L";
        PlaySafely(baseName + suffix, baseName);
    }

    // 有方向状态优先；不存在时回退到无方向基名（覆盖 Jump / Land）；都不存在则跳过，避免警告
    private void PlaySafely(string directional, string fallback)
    {
        if (animations == null) return;

        if (HasState(directional))   animations.Play(directional);
        else if (HasState(fallback)) animations.Play(fallback);
        // 两者都不存在：不调用 Play，避免 Animator 打印 "state does not exist" 警告
    }

    private bool HasState(string stateName)
        => animations.HasState(0, Animator.StringToHash(stateName));

}
