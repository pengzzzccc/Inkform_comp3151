using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// 链子材质包：多张材质，生成多条链时按下标循环取。
    /// 在 Assets > Create > Item > Chain Material Cue 创建，由 HangingChain 在 Inspector 里引用。
    /// </summary>
    [CreateAssetMenu(menuName = "Item/Chain Material Cue")]
    public class ChainMaterialCue : ScriptableObject
    {
        [Tooltip("链子材质表，多条链按下标循环取；空槽位 = 该条链用默认材质；全空 = 全部默认")]
        public Material[] materials;

        /// <summary>循环取第 index 条链的材质；数组为空或该槽空返回 null（调用方回落默认）。</summary>
        public Material PickMaterial(int index)
        {
            if (materials == null || materials.Length == 0) return null;
            return materials[index % materials.Length];
        }
    }
}
