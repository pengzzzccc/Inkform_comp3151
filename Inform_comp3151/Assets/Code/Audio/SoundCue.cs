using Inkform.Tool;
using UnityEngine;
using UnityEngine.Audio;

namespace Inkform.Audio
{
    /// <summary>
    /// 一条音效的配置：多个随机变体 + 音量/音高/并发限制，纯数据资产。
    /// 在 Assets > Create > Audio > Sound Cue 创建，由 AudioDirector 在 Inspector 里引用。
    /// 播放动作本身由 AudioManager 负责，本类只描述「该怎么播」。
    /// </summary>
    [CreateAssetMenu(menuName = "Audio/Sound Cue")]
    public class SoundCue : ScriptableObject
    {
        public AudioClip[] clips;                    // 多个变体，随机播放防听腻
        public AudioMixerGroup output;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("随机音高区间。别把任何一端设成 0 —— pitch 为 0 的源永远播不完，会永久占住池位")]
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        public bool loop;
        [Tooltip("同一 Cue 的最小重触发间隔，防止同帧叠加爆音")]
        public float cooldown = 0.05f;
        [Range(1, 8)] public int maxConcurrent = 3;

        [Header("Distance falloff")]
        // 默认关掉是刻意的：已有的 Cue 资产里没存这些字段，反序列化后走这里的初始值，
        // 行为和加距离感之前完全一致，不会因为本次改动突然集体变声。
        [Tooltip("关掉 = 恒按 2D 播。玩家自己的声音（跳/落/吃）应该关掉——它们永远在镜头中心，算距离没意义")]
        public bool spatial = false;
        [Tooltip("离听者超过这个距离就衰减到底")]
        public float falloffRange = 20f;
        [Tooltip("最远处的音量系数。0 = 完全静音")]
        [Range(0f, 1f)] public float minVolume = 0.15f;
        [Tooltip("混响档位上限。0 = 这条音效永不混响")]
        [Range(0f, 1f)] public float reverbAmount = 1f;
        [Tooltip("最远处的低通截止频率(Hz)，越低越闷。22000 = 不滤波")]
        public float minCutoff = 900f;

        // 运行时状态。ScriptableObject 是资产，实例常驻编辑器内存，这两个值
        // 不随退出播放模式清零 —— 由 AudioManager 在每次运行首次用到本 Cue 时重置。
        [System.NonSerialized] public float lastPlayTime = -999f;
        [System.NonSerialized] public int activeCount;

        /// <summary>随机取一个变体。数组里的空槽位会被跳过，全空则返回 null。</summary>
        public AudioClip PickClip() => RandomPick.FromArray(clips);

        public float PickPitch() =>
            Random.Range(pitchRange.x, pitchRange.y);
    }
}
