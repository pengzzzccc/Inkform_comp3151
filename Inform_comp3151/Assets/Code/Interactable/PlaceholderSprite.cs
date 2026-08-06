using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// 占位图生成：给还没有美术资源的关卡物件提供运行时可见的占位视觉。
    /// 懒生成 + 静态缓存（RopeGun.DiscSprite 同套路）—— 全局共用一份纹理。
    /// 有美术之后把 SpriteRenderer 上的 sprite 换成真图，本类自动退休。
    /// </summary>
    public static class PlaceholderSprite
    {
        private static Sprite bar;
        private static Sprite gear;

        /// <summary>白色竖条：激光柱占位。像素 32×128，配 transform scale 拉高。</summary>
        public static Sprite Bar
        {
            get
            {
                if (bar == null) bar = CreateBar();
                return bar;
            }
        }

        /// <summary>带 8 根辐条的圆环：齿轮占位。像素 128×128，转起来看得见。</summary>
        public static Sprite Gear
        {
            get
            {
                if (gear == null) gear = CreateGear();
                return gear;
            }
        }

        private static Sprite CreateBar()
        {
            const int w = 32, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var colors = new Color[w * h];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite CreateGear()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = size * 0.5f;
            float ringOuter = c - 2f;
            float ringInner = ringOuter * 0.72f;    // 齿环厚度 ≈ 28% 半径
            float hub = ringInner * 0.35f;          // 轴心圆

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                    bool on = d <= ringOuter && d >= ringInner;     // 齿环
                    on |= d <= hub;                                 // 轴心

                    if (!on)
                    {
                        // 辐条：8 根、均分 45°，从轴心到内圈。角度归一到本段再测偏差，
                        // |偏差| &lt; 0.06rad（≈3.4°）即落在辐条上
                        float ang = Mathf.Atan2(y + 0.5f - c, x + 0.5f - c);
                        float seg = Mathf.Abs(Mathf.Repeat(ang, Mathf.PI / 4f) - Mathf.PI / 8f);
                        if (d <= ringInner && d > hub && seg < 0.06f) on = true;
                    }

                    tex.SetPixel(x, y, on ? Color.white : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
