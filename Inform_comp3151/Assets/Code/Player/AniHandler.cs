using Inkform.player;
using Inkform.Bus;
using UnityEngine;

public class AniHandler : MonoBehaviour
{
    [SerializeField] private Animator animations;
    [SerializeField] private SpriteRenderer sprite;

    void Awake()
    {
        // clip 曲线的 path 是空串，SpriteRenderer 必定和 Animator 在同一个物体上
        if (sprite == null && animations != null) sprite = animations.GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        PlayerBus.StateChanged += OnState;
        PlayerBus.FaceChanged += OnFace;
        Refresh();                       // 用快照做首次同步
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
        if (animations == null) return;   // HasState 要用到，先挡住

        bool faceL = PlayerBus.Face == FaceDirection.L;

        // selfDirectional：状态名自带方向（贴哪面墙已由 PlayerHandler 按朝向选好），不加后缀也不翻转
        (string baseName, bool selfDirectional) = PlayerBus.State switch
        {
            PlayerState.Idle         => ("Idle",          false),
            PlayerState.Move         => ("Move",          false),
            PlayerState.JumpUp       => ("JumpUp",        false),
            PlayerState.Rise         => ("RiseUp",        false),
            PlayerState.Fall         => ("FallDown",      false),
            PlayerState.Land         => ("Land",          false),
            PlayerState.Eat          => ("Eat",           false),
            PlayerState.Release      => ("Release",       false),
            PlayerState.CeilingStick => ("Ceiling_Stick", false),
            PlayerState.CeilingMove  => ("Ceiling_Move",  false),
            PlayerState.CeilingIdle  => ("Ceiling_Idle",  false),
            PlayerState.WallSlideL   => ("Wall_Slide_L",  true),
            PlayerState.WallSlideR   => ("Wall_Slide_R",  true),
            _                        => ("Idle",          false),
        };

        if (selfDirectional) { Play(baseName, false); return; }

        string directional = baseName + (faceL ? "_L" : "_R");
        if (HasState(directional)) Play(directional, false);  // 有 _L/_R 镜像美术：朝向已画在帧里
        else                       Play(baseName, faceL);     // 只有一套朝右美术（RiseUp / Land）：用 flipX 补出朝左
    }

    // flip 与 clip 必须同帧生效，否则会闪一帧错误朝向。
    // 状态不存在就不调 Play，避免 Animator 打印 "state does not exist" 警告
    private void Play(string stateName, bool flip)
    {
        if (sprite != null) sprite.flipX = flip;
        if (HasState(stateName)) animations.Play(stateName);
    }

    private bool HasState(string stateName)
        => animations.HasState(0, Animator.StringToHash(stateName));

}
