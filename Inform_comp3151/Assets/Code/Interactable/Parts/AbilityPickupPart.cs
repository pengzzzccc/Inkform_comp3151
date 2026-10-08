using System.Collections.Generic;
using Inkform.Ability;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Retired ability card. Both card abilities are built-in defaults now (AbilityStore), so a
    /// placed card always finds its ability already owned and removes its world object on attach.
    /// Kept only because TimeCard / RopeGunCard are still placed in Mine Cave 2; delete the
    /// instances there, then this part and the two prefabs can go.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityPickupPart : MonoBehaviour, IInteractablePart
    {
        [Header("Ability Pickup")]
        [Tooltip("AbilityIds value this card stood for, e.g. 'checkpoint'. Owned = the card removes itself.")]
        [SerializeField] private string abilityId = AbilityIds.Checkpoint;

        private Interactable root;
        private readonly List<Collider2D> colliders = new List<Collider2D>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private bool consumed;

        public void Attach(Interactable interactable)
        {
            root = interactable;
            colliders.Clear();
            colliders.AddRange(root.GetComponentsInChildren<Collider2D>(true));
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));

            if (AbilityStore.Owns(abilityId)) ConsumeWorldObject();
        }

        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        private void ConsumeWorldObject()
        {
            if (consumed) return;
            consumed = true;
            foreach (Collider2D itemCollider in colliders)
            {
                if (itemCollider != null) itemCollider.enabled = false;
            }
            foreach (Renderer itemRenderer in renderers)
            {
                if (itemRenderer != null) itemRenderer.enabled = false;
            }

            if (root == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(root.gameObject);
            else
#endif
                Destroy(root.gameObject);
        }
    }
}
