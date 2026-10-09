using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// Turns an InputGlyphs.Glyph into its UI Toolkit element, so every place that shows a
    /// button — the tutorial hint, the menu prompt bar, the rebind table — draws it the same way:
    /// pad art at the sprite's own aspect, or a keycap (.key-cap in Theme.uss) carrying the
    /// key's name. Nothing it builds takes pointer input.
    /// </summary>
    public static class GlyphElements
    {
        public const float Height = 40f;        // matches --key-h
        public const float SmallHeight = 32f;   // matches .key--small

        public static VisualElement Create(InputGlyphs.Glyph glyph, bool small = false)
        {
            if (glyph.sprite != null)
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("key-icon");
                icon.style.backgroundImage = new StyleBackground(glyph.sprite);
                // Keep the sprite's aspect: shoulder and stick icons are wider than they are tall
                float height = small ? SmallHeight : Height;
                Rect r = glyph.sprite.rect;
                icon.style.height = height;
                icon.style.width = r.height > 0f ? height * r.width / r.height : height;
                return icon;
            }

            var key = new VisualElement { pickingMode = PickingMode.Ignore };
            key.AddToClassList("key-cap");
            if (small) key.AddToClassList("key--small");
            var name = new Label(glyph.label) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("key-cap-label");
            key.Add(name);
            return key;
        }
    }
}
