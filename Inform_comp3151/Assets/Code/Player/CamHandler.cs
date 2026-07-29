using Inkform.Bus;
using Inkform.player;
using UnityEngine;

/// <summary>
/// 跟随相机：平滑跟随目标，并按朝向做前瞻偏移。
/// 抖动和缩放 punch 是叠加在跟随结果之上的量，不写回基准位置，否则会和跟随互相打架、抖完回不去。
/// 抖动走非缩放时间、跟随走缩放时间 —— 于是 hitstop 期间画面「冻住但仍在震」。
/// </summary>
[RequireComponent(typeof(Camera))]
public class CamHandler : MonoBehaviour
{
    [Header("Follow setting")]
    [SerializeField] private Transform target;                  // 拖入 Player，与 InputHandler 的做法一致
    [SerializeField] private Vector2 followOffset = Vector2.zero;
    [SerializeField] private float followSmooth = 0.18f;        // 常规跟随的 SmoothDamp 时长
    [SerializeField] private float lookAhead = 1.2f;            // 朝向前瞻距离，0 = 关闭
    [SerializeField] private float lookAheadSmooth = 0.35f;

    [Header("Shake")]
    [SerializeField] private float traumaDecay = 1.6f;          // trauma 每秒衰减量
    [SerializeField] private float maxShakeOffset = 0.6f;       // trauma = 1 时的最大位移
    [SerializeField] private float shakeFrequency = 22f;

    private Camera cam;
    private float baseZ;
    private float baseOrthoSize;

    private Vector2 followVel;

    private float trauma;
    private float seedX, seedY;

    private float zoomAmount, zoomDuration;
    private UnscaledTimer zoomTimer;

    private float lookAheadNow, lookAheadVel;

    void Awake()
    {
        cam = GetComponent<Camera>();
        baseZ = transform.position.z;
        baseOrthoSize = cam.orthographicSize;

        // 两轴取不同噪声种子，否则 x/y 完全同相，抖起来是一条斜线
        seedX = Random.value * 1000f;
        seedY = Random.value * 1000f;
    }

    void OnEnable()
    {
        FxBus.ShakeRequested += OnShake;
        FxBus.ZoomRequested += OnZoom;
        FxBus.SnapRequested += SnapToTarget;
    }

    void OnDisable()
    {
        FxBus.ShakeRequested -= OnShake;
        FxBus.ZoomRequested -= OnZoom;
        FxBus.SnapRequested -= SnapToTarget;
    }

    void Start()
    {
        // 开局直接吸附到位，免得从 (0,0) 缓缓滑过去
        SnapToTarget();
    }

    /// <summary>立刻吸附到目标身上。开局和玩家被瞬移（复活）后共用这一条路径。</summary>
    private void SnapToTarget()
    {
        if (target == null) return;

        // 三个速度都必须归零：SmoothDamp 的速度是存在字段里的，不清的话
        // 吸附完这一帧就被残留惯性带着冲过头，看起来像「切过去又弹了一下」
        followVel = Vector2.zero;
        lookAheadVel = 0f;
        lookAheadNow = lookAhead * (PlayerBus.Face == FaceDirection.R ? 1f : -1f);

        Vector2 want = (Vector2)target.position + followOffset + new Vector2(lookAheadNow, 0f);
        transform.position = new Vector3(want.x, want.y, baseZ);
    }

    // 用 LateUpdate：玩家在 Update 里已经移动完，这一帧跟上去就不会有一帧的滞后抖动
    void LateUpdate()
    {
        Vector2 basePos = FollowStep();
        Vector2 shakeOffset = ShakeStep();
        ZoomStep();

        // z 必须恒定，否则会破坏 2D 渲染排序
        transform.position = new Vector3(basePos.x + shakeOffset.x, basePos.y + shakeOffset.y, baseZ);
    }

    private Vector2 FollowStep()
    {
        if (target == null) return transform.position;

        // 朝向前瞻：直接读总线快照，不需要持有 PlayerHandler 引用
        float wantAhead = lookAhead * (PlayerBus.Face == FaceDirection.R ? 1f : -1f);
        lookAheadNow = Mathf.SmoothDamp(lookAheadNow, wantAhead, ref lookAheadVel, lookAheadSmooth);

        Vector2 want = (Vector2)target.position + followOffset + new Vector2(lookAheadNow, 0f);
        return Vector2.SmoothDamp(transform.position, want, ref followVel, followSmooth);
    }

    private Vector2 ShakeStep()
    {
        trauma = Mathf.Max(0f, trauma - traumaDecay * Time.unscaledDeltaTime);
        if (trauma <= 0f) return Vector2.zero;

        // 平方：小 trauma 几乎无感，大的很猛，比线性有层次
        float shake = trauma * trauma;
        float t = Time.unscaledTime * shakeFrequency;

        // 用 Perlin 而不是纯随机：相邻帧连续，抖起来是晃动而不是抽搐
        return new Vector2(
            Mathf.PerlinNoise(seedX, t) * 2f - 1f,
            Mathf.PerlinNoise(seedY, t) * 2f - 1f) * (maxShakeOffset * shake);
    }

    private void ZoomStep()
    {
        if (zoomDuration <= 0f || !zoomTimer.IsRunning)
        {
            cam.orthographicSize = baseOrthoSize;
            return;
        }

        cam.orthographicSize = baseOrthoSize + zoomAmount * (zoomTimer.Remaining / zoomDuration);
    }

    // 累加而非覆盖：连爆会更猛，而不是每次都从头开始
    private void OnShake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    private void OnZoom(float amount, float duration)
    {
        if (duration <= 0f) return;

        zoomAmount = amount;
        zoomDuration = duration;
        zoomTimer.Set(duration);
    }
}
