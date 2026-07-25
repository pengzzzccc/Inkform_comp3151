using UnityEngine;
using Inkform.player;
using Inkform.Bus;

public class Bomb : ItemSuper
{
    private bool isAttacking = false;

    void OnEnable()
    {
        PlayerBus.StateChanged += OnState;
        OnState(PlayerBus.State);   // 用快照做首次同步
    }

    void OnDisable()
    {
        PlayerBus.StateChanged -= OnState;
    }

    void OnState(PlayerState state)
    {
        isAttacking = state == PlayerState.Eat;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.gameObject.CompareTag("Player") || !isAttacking) return;

        ItemBus.RaiseItemEaten(this);
        Destroy(this.gameObject);
    }
}
