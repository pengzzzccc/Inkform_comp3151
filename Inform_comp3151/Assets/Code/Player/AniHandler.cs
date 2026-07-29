using Inkform.player;
using Inkform.Bus;
using UnityEngine;

public class AniHandler : MonoBehaviour
{
    [SerializeField] private Animator animations;
    [SerializeField] private SpriteRenderer sprite;

    private string lastBase;    // 上一次播的基名（不含 _L/_R），用来判断能不能带着相位切过去

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

        // 基名没变 = 只是换了个镜像版本，带着当前相位切过去；换了动作才从头播。
        // 不带的话跑动中转身（Move_R → Move_L）会把走路循环拨回第 0 帧，脚会「弹」一下
        float phase = baseName == lastBase
            ? animations.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f
            : 0f;
        lastBase = baseName;

        if (selfDirectional) { Play(baseName, false, phase); return; }

        string directional = baseName + (faceL ? "_L" : "_R");
        if (HasState(directional)) Play(directional, false, phase);  // 有 _L/_R 镜像美术：朝向已画在帧里
        else                       Play(baseName, faceL, phase);     // 只有一套朝右美术（RiseUp / Land）：用 flipX 补出朝左
    }

    // flip 与 clip 必须同帧生效，否则会闪一帧错误朝向。
    // 状态不存在就不调 Play，避免 Animator 打印 "state does not exist" 警告
    private void Play(string stateName, bool flip, float phase)
    {
        if (sprite != null) sprite.flipX = flip;
        if (!HasState(stateName)) return;

        // 已经在播这个状态就别再 Play：RiseUp / Land 走的是 flipX 补朝向的分支，
        // 状态名不随朝向变，不挡的话每次转身都会把它整个重播一遍
        int hash = Animator.StringToHash(stateName);
        if (animations.GetCurrentAnimatorStateInfo(0).shortNameHash == hash) return;

        animations.Play(hash, 0, phase);
    }

    private bool HasState(string stateName)
        => animations.HasState(0, Animator.StringToHash(stateName));

}
