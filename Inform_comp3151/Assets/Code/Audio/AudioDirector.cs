using Inkform.Bus;
using Inkform.Item;
using Inkform.Player;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// 音频导演：把「游戏里发生了什么」翻译成「该放哪条音效」。
    /// 和 FxDirector 同一个套路 —— 订阅总线，把每个事件对应哪条 SoundCue 集中配在这一个
    /// Inspector 里，Bomb / PlayerHandler / BreakableWall 都不需要知道音频系统存在。
    /// 槽位留空是合法的：AudioManager 会静默跳过，之后往槽里丢条 Cue 就响，不用改代码。
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        [Header("Hazard")]
        [SerializeField] private SoundCue blast;        // 爆炸（每次爆炸恰好一次）
        [SerializeField] private SoundCue wallBreak;    // 可破坏墙碎裂
        [SerializeField] private SoundCue bombTick;     // 炸弹警戒帧推进一格（逼近预警 + 引信倒计时共用）

        [Header("RopeGun")]
        [SerializeField] private SoundCue grappleMode;  // 绳索枪模式切换（收缩/悬挂）

        [Header("Item")]
        [SerializeField] private SoundCue itemEaten;    // 吞下
        [SerializeField] private SoundCue itemSpit;     // 吐出

        [Header("Player")]
        [SerializeField] private SoundCue attack;       // 扑/吸的扬声，吃没吃到都放
        [SerializeField] private SoundCue jump;
        [SerializeField] private SoundCue land;

        // 死亡音不在这里：它按死因而不是按关注点分派（刺死和摔死该有不同的声音），
        // 所以那条 Cue 挂在 DeathStrategy 资产上，由策略自己播
        [Header("Life")]
        [SerializeField] private SoundCue respawn;      // 在检查点复活
        [SerializeField] private SoundCue checkpoint;   // 踩到检查点

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
            RopeGunBus.ModeChanged += OnGrappleMode;
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
            RopeGunBus.ModeChanged -= OnGrappleMode;
        }

        // 爆炸只能听 Blast —— Exploded 是在 foreach 里逐受害者发的，炸到 N 个就响 N 声
        private void OnBlast(Vector2 center, float radius, float force) => Play(blast, center);

        private void OnBroken(Vector2 center) => Play(wallBreak, center);

        // 玩家逼近和引信倒计时共用这一声。step / total 暂时用不上，
        // 留着是为了以后想「越接近音调越高」时不用再改总线签名
        private void OnTicked(Vector2 pos, int step, int total) => Play(bombTick, pos);

        // 绳索枪模式切换：模式参数暂时不用，切换出声就行
        private void OnGrappleMode(GrappleMode mode) => Play(grappleMode);

        private void OnItemEaten(ItemSuper item) => Play(itemEaten);

        // 这两条都是「玩家自己的声音」，恒在镜头中心，所以和 attack/jump/land 一样不传位置。
        // 对应的 Cue 资产里 spatial 应保持关闭 —— 见 SoundCue.cs 里那条 Tooltip
        private void OnRespawned(GameObject victim, Vector2 pos) => Play(respawn);

        private void OnCheckpointSet(Vector2 pos) => Play(checkpoint);

        // 吐出点恒在玩家身上 ≈ 镜头中心，衰减系数必然接近 1，传位置纯粹是为了
        // 「有位置就传下去」的一致性，实际听感和不传一样
        private void OnItemReleased(ItemSuper item, Vector2 pos, Vector2 velocity) => Play(itemSpit, pos);

        // PlayerBus 自己已经去重（只在状态真的变化时广播），所以这里不会每帧连发。
        // Release 不接：吐出声由 ItemBus.ItemReleased 负责，两边都接会重复。
        private void OnPlayerState(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Eat: Play(attack); break;
                case PlayerState.JumpUp: Play(jump); break;
                case PlayerState.Land: Play(land); break;
            }
        }

        // 位置只是「声音发生在哪」，衰减算不算、怎么算由 SoundCue 的 spatial 决定，
        // 没勾的 Cue 传了也不受影响。
        // 注意底层始终按 2D 播：这是 2D 游戏，一旦切成 Unity 的 3D 音就会走它默认的对数
        // 衰减（minDistance 1），而相机在 z = -10、它算出来的距离恒 ≥10，爆炸会被衰减到
        // 几乎听不见。AudioManager 自己按 XY 平面算距离，绕开了这个问题。
        private void Play(SoundCue cue, Vector3? position = null)
        {
            if (cue == null) return;                        // 槽位没配，静默跳过
            if (AudioManager.Instance == null) return;      // 场景里还没有 AudioManager
            AudioManager.Instance.Play(cue, position);
        }
    }
}
