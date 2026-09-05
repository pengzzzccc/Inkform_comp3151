using UnityEngine;
using UnityEngine.Tilemaps;

namespace Inkform.Fx
{
    /// <summary>
    /// One parallax stratum: a seamlessly repeating backdrop that follows the camera at its own
    /// rate. Content-agnostic — the layer can be a Tiled SpriteRenderer or a hierarchy of Tilemaps
    /// whose painted extent defines one repeat of the pattern ("paint it twice as wide, the cycle
    /// is twice as long").
    /// Position is anchored, not accumulated — pos = startPos + (camPos - camStart) * factor — so
    /// float drift never builds up, and camera teleports (respawn snap) re-align in a single frame.
    /// factor semantics: 0 = fixed in the world (the play plane), 1 = pinned to the screen
    /// (infinite distance), &gt;1 = slides past faster than the camera (foreground in front of play).
    /// </summary>
    [DefaultExecutionOrder(10)] // after CamHandler (order 0): reads the camera's final position incl. shake, same frame
    public class ParallaxLayer : MonoBehaviour
    {
        [Header("Parallax")]
        [Tooltip("Follow rate on X. 0 = world-fixed, 1 = screen-pinned, >1 = foreground passing by.")]
        [SerializeField] private float factorX = 0.5f;
        [Tooltip("Follow rate on Y. 1 = screen-pinned, the usual choice so layers never drift off vertically.")]
        [SerializeField] private float factorY = 1f;

        [Header("Seamless wrap")]
        [Tooltip("Wrap the layer back by whole tiles once it lags too far behind the camera. Only makes sense for seamless tiles or recurring decorations.")]
        [SerializeField] private bool wrapX = true;
        [SerializeField] private bool wrapY = true;
        [Tooltip("Repeat period in world units, 0 = auto. Auto resolves to the sprite's size, or for Tilemap layers to the painted extent. Set it to force a custom spacing.")]
        [SerializeField] private Vector2 tileSizeOverride = Vector2.zero;

        [Header("Setup")]
        [Tooltip("Defaults to Camera.main when left empty.")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("Extra world units added around the camera view when auto-sizing the tiled quad. Covers zoom punches.")]
        [SerializeField] private float zoomHeadroom = 1f;

        private SpriteRenderer spriteRenderer;   // optional — sprite-driven layers only
        private Vector2 tileLocal;      // one repeat period before transform scale
        private Vector2 tileWorld;      // one repeat period in world units, scale included
        private Vector2 startPos;       // anchors: where the layer and the camera sat when tracking began
        private Vector2 camStart;
        private bool anchored;

        // Pure math kept static so tests can verify the wrap algebra without a scene. Shifting a
        // seamless tiling by whole tiles is invisible, so WrapBranch snaps `desired` to the
        // congruent position (mod tile) nearest the camera — the layer stays within half a tile
        // of the camera forever, on a level of any length.
        public static float WrapBranch(float desired, float camera, float tile)
        {
            if (tile <= 0f) return desired;
            return desired + Mathf.Round((camera - desired) / tile) * tile;
        }

        public static Vector2 ComputePosition(Vector2 startPos, Vector2 camStart, Vector2 camPos,
            Vector2 factor, Vector2 tile, bool wrapX, bool wrapY)
        {
            Vector2 desired = startPos + (camPos - camStart) * factor;
            return new Vector2(
                wrapX ? WrapBranch(desired.x, camPos.x, tile.x) : desired.x,
                wrapY ? WrapBranch(desired.y, camPos.y, tile.y) : desired.y);
        }

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();   // optional — Tilemap layers have none

            // Tiled repeat relies on the texture's Repeat addressing, which wraps the WHOLE texture —
            // a sub-sprite of a sheet would sample its neighbours, not itself. So only sprites that
            // cover their entire texture may tile; sheet slices stay Simple (wrap still works for
            // them via tileSizeOverride as a recurrence spacing).
            if (CoversWholeTexture(spriteRenderer != null ? spriteRenderer.sprite : null))
            {
                spriteRenderer.drawMode = SpriteDrawMode.Tiled;
                spriteRenderer.tileMode = SpriteTileMode.Continuous;
            }
        }

        private static bool CoversWholeTexture(Sprite sprite)
        {
            return sprite != null && sprite.texture != null
                && Mathf.Approximately(sprite.rect.x, 0f)
                && Mathf.Approximately(sprite.rect.y, 0f)
                && Mathf.Approximately(sprite.rect.width, sprite.texture.width)
                && Mathf.Approximately(sprite.rect.height, sprite.texture.height);
        }

        void LateUpdate()
        {
            ResolveCamera();
            if (cameraTransform == null) return;   // e.g. a menu scene without a camera

            // Anchor on the first LateUpdate, not Awake: CamHandler snaps onto the player in Start,
            // and every Start runs before the first LateUpdate — anchoring here pairs the layer with
            // where the camera actually settles, not its authored prefab position.
            if (!anchored)
            {
                anchored = true;
                startPos = transform.position;
                camStart = cameraTransform.position;
                ComputePeriod();
                AutoSize();
                WarnIfPeriodMissing();
            }

            Vector2 p = ComputePosition(startPos, camStart, cameraTransform.position,
                new Vector2(factorX, factorY), tileWorld, wrapX, wrapY);
            // z untouched: the project keeps z constant or 2D render sorting breaks (see CamHandler)
            transform.position = new Vector3(p.x, p.y, transform.position.z);
        }

        private void ResolveCamera()
        {
            if (cameraTransform != null) return;
            Camera cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;
        }

        // One repeat period, per axis: explicit override > sprite size > union extent of every
        // Tilemap painted below this layer. A zero period simply disables wrap on that axis.
        private void ComputePeriod()
        {
            Vector2 auto = Vector2.zero;
            Vector3 scale = transform.lossyScale;

            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Vector2 size = spriteRenderer.sprite.bounds.size;          // pixels / PPU
                auto = new Vector2(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y));
            }
            else
            {
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                bool any = false;
                foreach (Tilemap tilemap in GetComponentsInChildren<Tilemap>())
                {
                    tilemap.CompressBounds();
                    Bounds local = tilemap.localBounds;
                    if (local.size.x <= 0f || local.size.y <= 0f) continue; // nothing painted yet

                    Vector3 worldCenter = tilemap.LocalToWorld(local.center);
                    Vector3 worldSize = Vector3.Scale(local.size, tilemap.transform.lossyScale);
                    min = Vector2.Min(min, (Vector2)worldCenter - (Vector2)worldSize * 0.5f);
                    max = Vector2.Max(max, (Vector2)worldCenter + (Vector2)worldSize * 0.5f);
                    any = true;
                }
                if (any) auto = max - min;
            }

            tileWorld = new Vector2(
                tileSizeOverride.x > 0f ? tileSizeOverride.x : auto.x,
                tileSizeOverride.y > 0f ? tileSizeOverride.y : auto.y);
            tileLocal = new Vector2(tileWorld.x / Mathf.Abs(scale.x), tileWorld.y / Mathf.Abs(scale.y));
        }

        private void WarnIfPeriodMissing()
        {
            if ((wrapX && tileWorld.x <= 0f) || (wrapY && tileWorld.y <= 0f))
                Debug.LogWarning(
                    $"[ParallaxLayer] '{name}' has wrap enabled but no repeat period — " +
                    "nothing is painted on its Tilemaps and it has no sprite or tileSizeOverride. " +
                    "The affected axis will follow without wrapping.", this);
        }

        // The layer itself only tracks the camera at `factor` rate, but wrapping keeps it within
        // half a tile of the camera, so a quad of tile + view + headroom always covers the view.
        // Sized once at anchor time — no per-frame mesh work. Tilemap layers size themselves.
        private void AutoSize()
        {
            if (spriteRenderer == null || spriteRenderer.drawMode != SpriteDrawMode.Tiled) return;
            Camera cam = cameraTransform.GetComponent<Camera>();
            if (cam == null || !cam.orthographic) return;

            // SpriteRenderer.size is LOCAL (pre-scale) — writing world units here silently
            // multiplies the quad by the transform scale (scale 4 turned 40 units into 160).
            Vector3 scale = transform.lossyScale;
            float halfW = cam.orthographicSize * cam.aspect + zoomHeadroom;
            float halfH = cam.orthographicSize + zoomHeadroom;

            // A non-wrapping axis must stay exactly one tile: this art loops horizontally only,
            // and any extra row would put a tiling seam inside the view.
            float widthLocal = wrapX ? (tileWorld.x + 2f * halfW) / Mathf.Abs(scale.x) : tileLocal.x;
            float heightLocal = wrapY ? (tileWorld.y + 2f * halfH) / Mathf.Abs(scale.y) : tileLocal.y;
            spriteRenderer.size = new Vector2(widthLocal, heightLocal);
        }
    }
}
