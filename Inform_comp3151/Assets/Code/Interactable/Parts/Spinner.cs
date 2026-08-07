using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Spin: constant self-rotation around the local Z axis (degrees/second). The collider rotates
    /// with the transform, so trigger detection follows — the basic motion of hazards like spinning
    /// gears. Negative values reverse direction.
    /// Pure driver: does not consume contact.
    /// </summary>
    public class Spinner : MonoBehaviour, IInteractablePart
    {
        [Header("Spin")]
        [Tooltip("Angular speed, degrees/second. Negative = reverse")]
        [SerializeField] private float angularSpeed = 120f;

        private Interactable root;

        public void Attach(Interactable root) => this.root = root;

        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            root.transform.Rotate(0f, 0f, angularSpeed * Time.deltaTime);
        }
    }
}
