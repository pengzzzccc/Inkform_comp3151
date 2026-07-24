using UnityEngine;
using Inkform.player;
using System;

public class Bomb : MonoBehaviour
{
    [SerializeField] private PlayerHandler player;
    private bool isAttacking = false;
    public event Action OnPlayerEatBomb;

    void OnEnable()
    {
        player.OnPlayerAction += HandleAction;
    }

    void OnDisable()
    {
        player.OnPlayerAction -= HandleAction;
    }

    void HandleAction(PlayerState state, FaceDirection face)
    {
        if (state == PlayerState.Attack)
            isAttacking = true;
        else
            isAttacking = false;

    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && isAttacking)
        {
            OnPlayerEatBomb?.Invoke();
            Destroy(this.gameObject);
        }
    }

}
