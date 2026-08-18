using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// What is held in the mouth: remembers the currently held item and spits it out on attack.
    /// The fourth layer split from PlayerHandler — the swallowing step is not here: that is the item's
    /// own decision (see CarriablePart.Swallow); this class only waits for the ItemBus "you ate it" notice.
    /// On death the held item is dropped back into the world; the mouth is empty after respawn.
    ///
    /// Attach to the Player.
    /// </summary>
    public class ItemCarrier : MonoBehaviour
    {
        [Header("Spit")]
        // 8-way spit (Q / right trigger): direction comes from the move input; the spawn point and
        // initial velocity both follow that direction. spitOffset must exceed "player collider half
        // width + item radius", or they overlap at spawn and physics pushes them apart
        [SerializeField] private float spitOffset = 0.9f;
        // Must exceed dash speed, or the spit catches itself from behind (for bombs, the immunity
        // window would expire and it would self-detonate in place)
        [SerializeField] private float spitSpeed = 24f;

        // The item held in the mouth (null = nothing). Stores the interface only, never the
        // implementation — "the player needs only interface storage, concrete logic lives in the
        // concrete object" (CarriablePart and the former Bomb both work).
        // "Does the mouth hold anything" is also snapshotted globally in ItemBus.Held; the former Bomb uses that one
        private ICarriable heldItem;

        /// <summary>Whether the mouth holds anything. Used when spitting with Q/right trigger.</summary>
        public bool IsEmpty => heldItem == null;

        void OnEnable()
        {
            ItemBus.ItemEaten += OnItemEaten;
            LifeBus.Died += OnDied;
        }

        void OnDisable()
        {
            ItemBus.ItemEaten -= OnItemEaten;
            LifeBus.Died -= OnDied;
        }

        /// <summary>
        /// Spits the held item in dir (8-way). Returns whether it actually spit — the caller decides
        /// from it whether to play Release or ignore.
        /// </summary>
        public bool TryRelease(Vector2 dir)
        {
            if (heldItem == null) return false;

            Vector2 mouth = (Vector2)transform.position + dir * spitOffset;
            ItemBus.RaiseItemReleased(heldItem, mouth, dir * spitSpeed);
            heldItem = null;
            return true;
        }

        // Called by ItemBus when an item is eaten: only a real eat enters the held state
        private void OnItemEaten(ICarriable item)
        {
            heldItem = item;
        }

        // Called by LifeBus when the player dies: drops the held item back into the world.
        // Without this, respawn carries an invisible, physics-less, fuse-less bomb stuck in the Held
        // phase (spitting goes through ItemReleased, which lights the fuse; a death drop must not, so
        // it is handled separately)
        private void OnDied(DeathContext ctx)
        {
            if (ctx.Victim != gameObject) return;
            if (heldItem == null) return;

            heldItem.DropAt(transform.position);
            heldItem = null;
            ItemBus.ClearHeld();
        }
    }
}
