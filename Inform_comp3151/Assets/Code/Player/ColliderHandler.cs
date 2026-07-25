using Inkform.player;
using Inkform.Bus;
using UnityEngine;

/// <summary>
/// 事件驱动的身体碰撞体切换：按 PlayerState 设置 BoxCollider2D 的 size/offset。
/// 按「状态」而非逐帧切换，稳定无抖动；默认用标准体，只有 overrides 里配置的状态换体型。
/// </summary>
public class ColliderHandler : MonoBehaviour
{
    [System.Serializable]
    public struct StateCollider
    {
        public PlayerState state;
        public Vector2 size;
        public Vector2 offset;
    }

    [SerializeField] private BoxCollider2D body;
    [SerializeField] private Vector2 defaultSize = Vector2.one;
    [SerializeField] private Vector2 defaultOffset = Vector2.zero;
    [SerializeField] private StateCollider[] overrides;

    void OnEnable()
    {
        PlayerBus.StateChanged += HandleState;
        HandleState(PlayerBus.State);    // 用快照做首次同步
    }

    void OnDisable()
    {
        PlayerBus.StateChanged -= HandleState;
    }

    private void HandleState(PlayerState state)
    {
        if (body == null) return;

        Vector2 size = defaultSize;
        Vector2 offset = defaultOffset;
        if (overrides != null)
        {
            foreach (var o in overrides)
            {
                if (o.state == state)
                {
                    size = o.size;
                    offset = o.offset;
                    break;
                }
            }
        }

        body.size = size;
        body.offset = offset;
    }
}
