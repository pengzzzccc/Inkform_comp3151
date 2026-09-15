using UnityEngine;

namespace Inkform.UI
{
    /// <summary>
    /// The tutorial's page sheets, replacing the sprite arrays the Tutorial prefab used to carry.
    /// Authored via the UIToolkitBootstrap-generated asset at Assets/Resources/UI/TutorialPages.asset
    /// (page images pre-filled from Art/UI/Tutirial), loaded by the UIManager at startup. A
    /// missing asset simply means pickups never open a tutorial — the abilities still unlock,
    /// same as the old "unwired prefab" behaviour.
    /// </summary>
    public class TutorialPages : ScriptableObject
    {
        [Tooltip("Unchecked = pickups never open this tutorial (they still unlock as usual)")]
        public bool showOnPickup = true;

        [Tooltip("Pages shown after picking up the timecard (checkpoint ability). One sprite = one page")]
        public Sprite[] checkpointPages;

        [Tooltip("Pages shown after picking up the rope gun card. One sprite = one page")]
        public Sprite[] ropeGunPages;
    }
}
