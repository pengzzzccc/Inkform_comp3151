using System;
using Inkform.Bus;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// A region of the room that owns its own parallax: one component (plus its trigger
    /// BoxCollider2D) on a root object, with each backdrop depth as a plain child transform
    /// listed in <see cref="strata"/>. The strata are always visible and sit still wherever the
    /// author placed them; while the player is inside the box each tracks the camera at its own
    /// factor, and on leaving it simply freezes where it was. No fades, no visibility toggling.
    ///
    /// The authored layout is the composition the player sees standing dead-centre in the zone:
    /// displacement is anchored to the zone's centre —
    /// pos = authoredPos + (camPos - zoneCentre) * factor — so with the camera at the centre
    /// every stratum sits exactly where it was placed in the editor, and the parallax offsets
    /// grow from that reference as the player moves off-centre. The formula is stateless (it
    /// depends only on the current camera position), so entering/leaving needs no re-anchoring;
    /// camera teleports (respawn snap) are correct for free; and it reuses the global
    /// ParallaxLayer's math with the depth vocabulary to match: 0 = fixed in the world,
    /// 1 = pinned to the screen, &gt;1 = foreground passing by.
    ///
    /// The same authoring pattern as CameraLimit — a scene-placed component the player interacts
    /// with through Unity physics, no registration, no manager. Zones may overlap: each drives
    /// only the strata it lists. The global ParallaxBackground keeps running untouched
    /// underneath: local strata are additive, sorted by their own sorting orders.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class LocalParallaxZone : MonoBehaviour
    {
        /// <summary>One backdrop depth: a child transform plus its parallax factors. The stratum
        /// carries no component of its own — the zone owns every write to it.</summary>
        [Serializable]
        public class Stratum
        {
            [Tooltip("The child transform that slides. One entry per backdrop depth.")]
            public Transform target;
            [Tooltip("Follow rate on X. 0 = world-fixed, 1 = screen-pinned, >1 = foreground.")]
            public float factorX = 0.5f;
            [Tooltip("Follow rate on Y. 1 = screen-pinned, so layers never drift off vertically.")]
            public float factorY = 1f;

            // Written by the zone at Awake: where the stratum sits as authored — the composition
            // the player sees with the camera at the zone's centre.
            [NonSerialized] public Vector2 authoredPos;
        }

        [Tooltip("One entry per backdrop depth, each pointing at a child transform.")]
        [SerializeField] private Stratum[] strata = Array.Empty<Stratum>();
        [Tooltip("Defaults to Camera.main when left empty.")]
        [SerializeField] private Transform cameraTransform;

        private bool inside;
        private Vector2 zoneCentre;            // the trigger box's world centre — the parallax reference
        private BoxCollider2D box;

        void Awake()
        {
            // The zone is a detector, never a wall — flip the collider to trigger so the author
            // cannot accidentally ship a solid box.
            box = GetComponent<BoxCollider2D>();
            box.isTrigger = true;

            // The authored composition and the centre it is anchored to. Cached once: zones and
            // their strata are static set dressing, nothing moves them except this component.
            zoneCentre = box.bounds.center;
            foreach (Stratum stratum in strata)
                if (stratum?.target != null) stratum.authoredPos = stratum.target.position;
        }

        void OnEnable()
        {
            LifeBus.Died += OnPlayerDied;
            LifeBus.Respawned += OnPlayerRespawned;
        }

        void OnDisable()
        {
            LifeBus.Died -= OnPlayerDied;
            LifeBus.Respawned -= OnPlayerRespawned;
            inside = false;   // disabled mid-visit (scene teardown): stop writing to the strata
        }

        void LateUpdate()
        {
            if (!inside) return;   // outside the zone the strata simply sit still

            if (cameraTransform == null) return;

            // pos = authoredPos + (camPos - zoneCentre) * factor: with the camera at the zone's
            // centre every stratum rests at its authored spot; offsets grow from there.
            Vector2 camPos = cameraTransform.position;
            foreach (Stratum stratum in strata)
            {
                if (stratum?.target == null) continue;

                Vector2 p = ParallaxLayer.ComputePosition(stratum.authoredPos, zoneCentre, camPos,
                    new Vector2(stratum.factorX, stratum.factorY), Vector2.zero, wrapX: false, wrapY: false);
                // z untouched: the project keeps z constant or 2D render sorting breaks (see CamHandler)
                Vector3 current = stratum.target.position;
                stratum.target.position = new Vector3(p.x, p.y, current.z);
            }
        }

        // ---- Enter / leave (driven by the triggers and the LifeBus safety nets) ----

        // Stateless anchoring: entering just starts the LateUpdate writes — the displacement
        // formula reads only the current camera position, so there is nothing to capture here.
        private void Enter()
        {
            ResolveCamera();
            inside = true;
        }

        private void ResolveCamera()
        {
            if (cameraTransform != null) return;
            Camera cam = Camera.main;
            if (cam != null) cameraTransform = cam.transform;
        }

        // ---- Player detection ----

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (inside || !other.CompareTag(Tags.Player)) return;
            Enter();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!inside || !other.CompareTag(Tags.Player)) return;
            inside = false;   // the strata freeze where they are — nothing else to do
        }

        // A death teleports the player out (or disables their collider during the burn-away);
        // the physics exit callback can be skipped entirely, which would leave the strata
        // tracking a camera that walks the run back to the checkpoint. Freeze them here.
        private void OnPlayerDied(DeathContext ctx) => inside = false;

        // The respawn drop-off point may sit inside this zone (a checkpoint placed in the
        // cavern): the teleport produces no enter callback while colliders overlap statically,
        // so re-probe once against the player's position.
        private void OnPlayerRespawned(GameObject victim, Vector2 pos)
        {
            if (inside || box == null) return;

            Bounds zone = box.bounds;
            if (pos.x < zone.min.x || pos.x > zone.max.x || pos.y < zone.min.y || pos.y > zone.max.y)
                return;

            Enter();
        }

        // ---- Authoring ----

        private void OnDrawGizmos()
        {
            // Bounds only exist with a collider; the RequireComponent guarantees one in play
            // mode, but the gizmo must also draw before the component is configured.
            BoxCollider2D collider = GetComponent<BoxCollider2D>();
            if (collider == null) return;

            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.25f);
            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.8f);
            Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
        }
    }
}
