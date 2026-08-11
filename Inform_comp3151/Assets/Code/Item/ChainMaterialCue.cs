using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Chain material pack: multiple materials; when generating multiple chains they are picked
    /// cyclically by index. Created via Assets > Create > Item > Chain Material Cue, referenced by
    /// HangingChain in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Item/Chain Material Cue")]
    public class ChainMaterialCue : ScriptableObject
    {
        [Tooltip("Chain material list, picked cyclically by chain index; an empty slot = that chain uses the default material; all empty = all default")]
        public Material[] materials;

        /// <summary>Cyclically picks the material for chain index; returns null when the array is empty
        /// or that slot is empty (the caller falls back to default).</summary>
        public Material PickMaterial(int index)
        {
            if (materials == null || materials.Length == 0) return null;
            return materials[index % materials.Length];
        }
    }
}
