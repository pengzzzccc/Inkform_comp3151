using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Standable surface: a normal solid object you can walk, jump on, and touch (platform / crate /
    /// food box). Standing is done naturally by "layer + non-trigger collider + rigidbody"; this
    /// component only does two things:
    /// ① semantic marker — a level designer sees at a glance that this thing can be stood on;
    /// ② config validation — at Attach it checks layer and collider type on the spot and warns on misconfiguration.
    ///
    /// Layer constraint: must be Terrain(6) or Breakable(11) — the player's four-way contact probe
    /// (ContactSensor.terrainMask) and the rope gun's terrain hits (RopeGun.hitMask) only recognize
    /// these two layers; anything else cannot be stood on and hooks pass straight through.
    ///
    /// Moving platform: with PatrolMover + Rigidbody2D (Kinematic), players standing on top
    /// automatically follow the platform (PlayerMotor does a delta follow from the contacted
    /// rigidbody's movement; this component needs no extra logic).
    /// </summary>
    public class SolidSurface : MonoBehaviour, IInteractablePart
    {
        // Keep in sync with ContactSensor.terrainMask / RopeGun.hitMask — all three must change together
        private const int LayerTerrain = 6;
        private const int LayerBreakable = 11;

        public void Attach(Interactable root)
        {
            int layer = root.gameObject.layer;
            if (layer != LayerTerrain && layer != LayerBreakable)
            {
                Debug.LogWarning($"{root.name} has SolidSurface but its layer is {layer} ({LayerMask.LayerToName(layer)}): " +
                                 "it must be Terrain(6) or Breakable(11), or the player cannot stand on it and rope hooks pass through", root);
            }

            Collider2D col = root.GetComponent<Collider2D>();
            if (col == null)
            {
                Debug.LogWarning($"{root.name} has SolidSurface but no Collider2D", root);
            }
            else if (col.isTrigger)
            {
                Debug.LogWarning($"{root.name}'s collider has IsTrigger checked: the player will pass straight through. " +
                                 "SolidSurface needs a non-trigger collider", root);
            }
        }

        // Pure marker: does not consume contact
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;
    }
}
