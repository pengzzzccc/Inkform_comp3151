using Inkform.Bus;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 可叼 part：物体能被绳索枪命中后吞下、叼在嘴里、吐出、死亡掉落。
    /// Bomb.Swallow / DropAt / OnItemReleased 的公共行为抽出来（去掉炸弹特有的引信）——
    /// 「吞下后放回世界」这套生命周期对任何可叼物都一样，引爆之类才是具体物体的私事。
    ///
    /// 玩家侧只认 ICarriable 接口：ItemCarrier / ItemBus / RopeGun 都不认识本类。
    /// 要求：Interactable 本物体上有 Rigidbody2D（隐藏时关 simulated）。
    /// 注：挂在子物体上 RopeGun 的 GetComponent 探测不到 —— 必须挂在本物体。
    /// </summary>
    public class CarriablePart : MonoBehaviour, IInteractablePart, ICarriable
    {
        [Header("Carriable")]
        [Tooltip("能不能被吞下（绳索枪拉到后）")]
        [SerializeField] private bool eatAble = true;
        [Tooltip("吐出后短暂免疫玩家接触，防止刚吐出去就被引爆；<= 0 关闭")]
        [SerializeField] private float spitArmTime = 0.3f;

        private Interactable root;
        private Rigidbody2D body;
        private Collider2D hitBox;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private bool held;
        private Timer spitTimer;    // 吐出免疫计时：只对玩家接触短路

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} 挂了 CarriablePart 但没挂 Rigidbody2D，无法被吞吃", root);

            hitBox = root.GetComponent<Collider2D>();
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
        }

        void OnEnable() { ItemBus.ItemReleased += OnItemReleased; }
        void OnDisable() { ItemBus.ItemReleased -= OnItemReleased; }

        // 拉取中（ropeGrappled）接触玩家：吞，而不是让爆炸类 part 引爆（Bomb 同款优先级）。
        // 吞失败（嘴满等）不短路 —— 放行给后续 part，Bomb 原版嘴满拉过来也是炸
        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;

            // 吐出后的免疫期：吞掉玩家接触，防止刚吐出去就被爆炸触发引爆（Bomb ArmTimer 同款）。
            // 只拦玩家 —— 撞地撞墙的速度爆炸不受影响
            if (spitTimer.IsRunning && other.CompareTag(Tags.Player)) return true;

            if (!ropeGrappled) return false;
            if (!other.CompareTag(Tags.Player)) return false;
            return TrySwallowByRope(other.transform);
        }

        // ---- ICarriable ----

        public bool TrySwallowByRope(Transform player)
        {
            if (held) return false;
            if (!eatAble) return false;
            if (ItemBus.Held != null) return false;     // 嘴里已经有东西

            Swallow(player);
            return true;
        }

        // 吞下：不销毁，只关物理 + 关显示挂到玩家身上，等着被吐出来。
        // 注意不能 SetActive(false)：OnDisable 会退订总线，就收不到「吐出」了
        private void Swallow(Transform player)
        {
            held = true;
            ropeGrappled = false;
            body.simulated = false;
            hitBox.enabled = false;
            SetVisible(false);
            transform.SetParent(player, false);
            transform.localPosition = Vector3.zero;

            // 被吞 = 切断所有挂绳：悬挂可叼物吐出后是自由物体（Bomb 同款语义）
            if (root.TryGetPart(out HangingChain hanging)) hanging.CutAllChains();

            ItemBus.RaiseItemEaten(this);
        }

        /// <summary>玩家死亡时被放回世界：原地放下，不点引信、不给初速。</summary>
        public void DropAt(Vector2 pos)
        {
            held = false;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // 工程里 m_AutoSyncTransforms = 0：transform 和刚体位置互不同步，两个都要写
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        // 被玩家吐出来：放回世界、给初速度。
        // 不点引信 —— 引信是炸弹的私有行为，「吐出后干什么」由具体物体自己决定
        private void OnItemReleased(ICarriable item, Vector2 pos, Vector2 velocity)
        {
            if (item != (ICarriable)this) return;       // 吐的不是我

            held = false;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // 工程里 m_AutoSyncTransforms = 0：transform 和刚体位置互不同步，两个都要写
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = velocity;
            body.angularVelocity = 0f;

            // 吐出保护：吐出瞬间初速高、玩家往往紧贴，这期间接触玩家不触发任何爆炸
            if (spitArmTime > 0f) spitTimer.Set(spitArmTime);
        }

        // 拉取中标记：被绳索枪命中后玩家被拉过来，期间接触玩家必须吞、不能炸（Bomb 同款）
        private bool ropeGrappled;

        public void MarkRopeGrappled() => ropeGrappled = true;
        public void ClearRopeGrappled() => ropeGrappled = false;

        // 自带去重：隐藏/显示与 Bomb 的 SetVisible 同一套（逐帧驱动也不会重复写）
        private bool _visible = true;

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            foreach (Renderer r in renderers) r.enabled = visible;
        }
    }
}
