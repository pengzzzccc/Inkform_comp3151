using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// 占位视觉：SpriteRenderer 没配 sprite 时自动补一张运行时生成的占位图。
    /// 纯开发辅助（不是 part，不参与交互分发）：有美术资源后把 sprite 换上即可，
    /// 本组件留着也不会再覆盖（只补空 sprite）。
    /// </summary>
    public enum PlaceholderShape { Bar, Gear }

    public class PlaceholderVisual : MonoBehaviour
    {
        [SerializeField] private PlaceholderShape shape = PlaceholderShape.Bar;

        void Awake()
        {
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite != null) return;
            sr.sprite = shape == PlaceholderShape.Gear ? PlaceholderSprite.Gear : PlaceholderSprite.Bar;
        }
    }
}
