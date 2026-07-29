using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 音频池：开局建好一批 AudioSource 循环复用，避免每次播音都 new GameObject。
/// 播放策略（随机变体 / 冷却 / 并发上限 / 距离衰减）全在 SoundCue 里配，这里只管取源和还源。
/// 挂在场景里任意物体上即可，自身跨场景保留。
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // 低通滤波器的「不滤波」档。人耳上限约 20kHz，设在这之上等于整条通带全放行
    private const float FullBandwidth = 22000f;

    [SerializeField] private int poolSize = 24;

    [Tooltip("混响档位，由近到远。留空 = 不做混响，音量和低通照常生效")]
    [SerializeField] private AudioMixerGroup[] reverbTiers;

    private readonly Queue<Source> pool = new Queue<Source>();
    private readonly List<Voice> active = new List<Voice>();

    // SoundCue 的运行时计数存在资产上，不随退出播放模式清零；上一次运行若泄漏了
    // activeCount，下次运行该 Cue 会永久静音。首次用到时归零即可绕开。
    private readonly HashSet<SoundCue> seen = new HashSet<SoundCue>();

    // 源和它的低通滤波器必须成对存：每次播放都要按距离重设 cutoff，
    // 逐次 GetComponent 太浪费，建池时取一次存下来
    private struct Source
    {
        public AudioSource src;
        public AudioLowPassFilter lpf;
    }

    // 回收时要知道该给哪个 Cue 减计数，所以源和 Cue 必须成对存
    private struct Voice
    {
        public Source source;
        public SoundCue cue;
    }

    // 听者位置。本组件挂在 DontDestroyOnLoad 物体上、不跟着相机走，所以不能像
    // FxDirector 那样直接用 transform.position（那个挂在相机上）。
    // 换场景后旧 listener 会变成 Unity 的 fake-null，下次取用时自动重找
    private Transform listenerCache;

    private Transform Listener
    {
        get
        {
            if (listenerCache == null)
            {
                // 用 Any 而不是 First：场景里本来就只该有一个启用的 AudioListener，没有「第几个」可言
                AudioListener l = FindAnyObjectByType<AudioListener>();
                listenerCache = l != null ? l.transform : null;
            }
            return listenerCache;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = new GameObject($"AudioSource_{i}");
            go.transform.SetParent(transform);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;

            // 一律 2D，传了位置也不切 3D。Unity 的 3D 衰减会叠在我们自己算的距离系数
            // 之上，而相机恒在 z = -10、它算出来的距离永远 ≥10，爆炸会被压到几乎听不见。
            // 距离感完全由 Falloff() 按 XY 平面算，z 轴不参与。
            src.spatialBlend = 0f;

            AudioLowPassFilter lpf = go.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = FullBandwidth;

            pool.Enqueue(new Source { src = src, lpf = lpf });
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 播完自动回池。不用「按 clip 时长起协程」是因为：手动停掉一个源之后它会立刻
    // 回池、可能被别的 Cue 取走，那条旧协程到期就会停掉新声音、还把错的 Cue 计数减一。
    // isPlaying 没有这个问题；loop 声音 isPlaying 恒为真，会自然留到显式 Stop 为止。
    void Update()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (active[i].source.src == null) { active.RemoveAt(i); continue; }   // 源被意外销毁
            if (!active[i].source.src.isPlaying) ReleaseAt(i);
        }
    }

    /// <summary>播一条 Cue。position 为 null（或 Cue 没勾 spatial）时不做距离衰减，
    /// 否则按离听者多远来压音量、加混响、削高频。
    /// 被冷却/并发上限挡下、或池子已空时返回 null（直接丢弃，不扩容）。</summary>
    public AudioSource Play(SoundCue cue, Vector3? position = null)
    {
        if (cue == null) return null;

        if (seen.Add(cue))          // 本次运行第一次见到它，清掉上次运行残留的计数
        {
            cue.activeCount = 0;
            cue.lastPlayTime = -999f;
        }

        // 冷却必须走非缩放时间：爆炸会触发 hitstop 把 timeScale 压到 0，
        // 用 Time.time 的话卡帧期间冷却根本不推进
        if (Time.unscaledTime - cue.lastPlayTime < cue.cooldown) return null;
        if (cue.activeCount >= cue.maxConcurrent) return null;
        if (pool.Count == 0) return null;   // 池空了直接丢弃，别扩容

        AudioClip clip = cue.PickClip();
        if (clip == null) return null;      // Cue 没配片段，或者槽位全是空的

        Source s = pool.Dequeue();
        AudioSource src = s.src;

        src.clip = clip;
        src.pitch = cue.PickPitch();
        src.loop = cue.loop;

        // 距离系数：0 = 贴在听者脸上，1 = 远到该衰减到底。三种表现共用这一个值
        float t = Falloff(cue, position);

        src.volume = cue.volume * Mathf.Lerp(1f, cue.minVolume, t);
        s.lpf.cutoffFrequency = Mathf.Lerp(FullBandwidth, cue.minCutoff, t);
        src.outputAudioMixerGroup = PickGroup(cue, t);

        src.Play();
        cue.lastPlayTime = Time.unscaledTime;
        cue.activeCount++;
        active.Add(new Voice { source = s, cue = cue });

        return src;
    }

    /// <summary>离听者有多远，归一化到 [0,1]。没勾 spatial / 没传位置 / 场景里还没有
    /// AudioListener 时一律返回 0，也就是「就在耳边」——退化成加距离感之前的行为。</summary>
    private float Falloff(SoundCue cue, Vector3? position)
    {
        if (!cue.spatial || !position.HasValue || cue.falloffRange <= 0f) return 0f;

        Transform ear = Listener;
        if (ear == null) return 0f;

        // 只量 XY 平面（Vector2 转换会丢掉 z）：相机在 z = -10，把 z 算进去的话
        // 哪怕声音就在玩家脚下，距离也有 10 个单位起步
        return Mathf.Clamp01(Vector2.Distance(position.Value, ear.position) / cue.falloffRange);
    }

    /// <summary>按距离挑混响档位。混响强度只能做成离散档 —— AudioMixer 的效果参数是
    /// group 级别的，同一个 group 里所有声音共享一份设置，没法每个声音各算各的。</summary>
    private AudioMixerGroup PickGroup(SoundCue cue, float t)
    {
        // 留空是合法的：降级成只有音量和低通，距离感照样在。这样没建 mixer 也能先跑，
        // 建好之后拖进来就自动生效，和本项目「槽位留空静默跳过」的一贯做法一致
        if (!cue.spatial || cue.reverbAmount <= 0f) return cue.output;
        if (reverbTiers == null || reverbTiers.Length == 0) return cue.output;

        int tier = Mathf.Min((int)(t * cue.reverbAmount * reverbTiers.Length), reverbTiers.Length - 1);
        return reverbTiers[tier] != null ? reverbTiers[tier] : cue.output;
    }

    /// <summary>提前停掉一个还在播的源。loop 声音只能靠这个收场（它永远不会自然播完）。
    /// 不需要调用方传 Cue —— 传错就会减错计数，这里从 Voice 里自己取。</summary>
    public void Stop(AudioSource src)
    {
        if (src == null) return;

        int i = active.FindIndex(v => v.source.src == src);
        if (i < 0) return;              // 不是本池发出去的，或者已经回收过了
        ReleaseAt(i);
    }

    private void ReleaseAt(int i)
    {
        Voice v = active[i];
        active.RemoveAt(i);

        v.source.src.Stop();
        v.source.src.clip = null;

        // 必须还原：源是循环复用的，一次远处爆炸把 cutoff 压到几百 Hz 之后不还原，
        // 下一个借到这个源的近处音效（比如玩家跳跃声）会莫名其妙变闷。
        // outputAudioMixerGroup 不用还原 —— 每次 Play 都会显式重设
        v.source.lpf.cutoffFrequency = FullBandwidth;

        if (v.cue != null) v.cue.activeCount--;
        pool.Enqueue(v.source);
    }
}
