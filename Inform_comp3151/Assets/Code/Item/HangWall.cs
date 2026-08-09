using Inkform.Tool;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Hinged wall: one end rotates around a world-fixed hinge axis, the other end is held up by a
    /// configurable chain.
    ///
    /// The entire physical structure is assembled at runtime in Awake; the scene only needs the body
    /// and anchors placed:
    /// ① HingeJoint2D — the hinge axis is the wall's local hingePivot position, connectedBody = null
    ///    (world-fixed axis; the anchor is the axis end's initial world position when the wall was
    ///    placed), the wall swings under gravity and can be stood on / pushed;
    /// ② Chain — from the scene-placed chainAnchor down to the wall's attachPoint (Verlet attached-body
    ///    mode), holding up the free end when taut.
    ///
    /// The chain is configurable: full Chain.Settings (segment length/count/iterations/gravity/
    /// damping/segment collision/width/sorting). The chain is severable: blast waves (Chain subscribes
    /// HazardBus.Blast) and rope-gun shots (the Chain.Active static registry is iterated by
    /// RopeGun.CutChainsNearHook) can cut it; once fully severed the wall is left with only the hinge
    /// constraint, swinging freely. Severed chains are unrecoverable — same as Bomb's hanging mode.
    ///
    /// Layer suggestion: put on Terrain(6) / Breakable(11) — the player's four-way contact probe and
    /// the rope gun's terrain hits only recognize these two layers
    /// (ContactSensor.terrainMask / RopeGun.hitMask).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class HangWall : MonoBehaviour
    {
        [Header("Hinge")]
        [Tooltip("Hinge axis position on the wall (local coordinates, relative to center of mass = the collider center by default). E.g. left end = (-half-width, 0)")]
        [SerializeField] private Vector2 hingePivot = new Vector2(-1f, 0f);
        [Tooltip("Angle limit around the hinge (degrees, relative to the initial angle). 0 = unlimited — normally the swing range is decided by chain slack and collisions")]
        [SerializeField] private float angleLimit = 0f;

        [Header("Chain")]
        [Tooltip("Chain attach position (local coordinates, relative to center of mass). E.g. right end = (half-width, 0)")]
        [SerializeField] private Vector2 attachPoint = new Vector2(1f, 0f);
        [Tooltip("Fixed chain anchor (an empty object placed in the scene, drag it in the Inspector). Right-click this component → Create Chain Anchor auto-generates one")]
        [SerializeField] private Transform chainAnchor;
        [Tooltip("Chain config: segment length/count/iterations/gravity/damping/segment collision/width/sorting")]
        [SerializeField] private Chain.Settings chainSettings = new Chain.Settings();

        [Header("Editor")]
        [Tooltip("Editor only: Create Chain Anchor generates the anchor at this height directly above the attach point")]
        [SerializeField] private float anchorHeight = 3f;

        private Rigidbody2D body;
        private HingeJoint2D hinge;
        private Chain chain;

        /// <summary>Runtime-readable: whether the chain is intact (not severed).</summary>
        public bool ChainIntact => chain != null && chain.IsIntact;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            BuildHinge();
            BuildChain();
        }

        // Hinge: world-fixed axis. connectedAnchor = the axis end's world position when the wall was
        // placed; the wall rotates around it from then on
        private void BuildHinge()
        {
            hinge = gameObject.AddComponent<HingeJoint2D>();
            hinge.anchor = hingePivot;
            hinge.connectedBody = null;         // anchor pinned to the world, does not move with any object
            hinge.connectedAnchor = (Vector2)transform.TransformPoint(hingePivot);

            if (angleLimit > 0f)
            {
                hinge.useLimits = true;
                hinge.limits = new JointAngleLimits2D { min = -angleLimit, max = angleLimit };
            }
        }

        // Chain: from the fixed anchor down to the wall's free-end attach point, Verlet attached-body
        // mode (offset attach point, not the center of mass)
        private void BuildChain()
        {
            if (chainAnchor == null)
            {
                Debug.LogWarning($"{name} has no chainAnchor configured: the wall will only be held by the hinge. Drag in an anchor or right-click to run Create Chain Anchor", this);
                return;
            }

            GameObject chainGo = new GameObject("HangChain");
            chainGo.transform.SetParent(transform, false);

            chain = chainGo.AddComponent<Chain>();
            chain.Configure(chainSettings);
            chain.Init(body, chainAnchor.position, attachPoint);
        }

        // ---- Editor helpers ----

        // Draws the axis/attach point/chain hints in the Scene view so level designers see at a glance
        // how the wall will move
        void OnDrawGizmosSelected()
        {
            Vector3 pivot = transform.TransformPoint(hingePivot);
            Vector3 attach = transform.TransformPoint(attachPoint);

            // Hinge axis: orange cross — the wall rotates around this point
            Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
            float s = 0.4f;
            Gizmos.DrawLine(pivot + Vector3.left * s, pivot + Vector3.right * s);
            Gizmos.DrawLine(pivot + Vector3.down * s, pivot + Vector3.up * s);
            Gizmos.DrawWireSphere(pivot, s * 0.6f);

            // Attach point: cyan circle
            Gizmos.color = new Color(0.2f, 0.9f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(attach, 0.18f);

            // Chain: dashed hint from anchor to attach point
            if (chainAnchor != null)
            {
                Gizmos.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
                Gizmos.DrawLine(chainAnchor.position, attach);
            }
        }

        // Generates a fixed anchor empty object directly above the attach point and wires the
        // reference, saving manual placement
        [ContextMenu("Create Chain Anchor")]
        private void CreateChainAnchor()
        {
            if (chainAnchor != null)
            {
                Debug.LogWarning($"{name} already has a chainAnchor; clear the reference first", this);
                return;
            }

            GameObject go = new GameObject($"{name}_ChainAnchor");
            go.transform.position = transform.TransformPoint(attachPoint) + Vector3.up * anchorHeight;
            chainAnchor = go.transform;

            Debug.Log($"Anchor {go.name} generated and wired into chainAnchor", go);
        }
    }
}
