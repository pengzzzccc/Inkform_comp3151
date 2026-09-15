using UnityEngine;

namespace Inkform.UI
{
    /// <summary>
    /// UnityEngine.Object stand-in used as the scoped-lock owner for panels that are plain C#
    /// objects. The lock tables (InputHandler.SetGameplayInputLocked / GameTimeController
    /// SetWorldFrozen) key owners by reference and prune destroyed ones, so the owner has to be
    /// a UnityEngine.Object — the Toolkit panels themselves are not. UIManager creates one
    /// instance per keyed purpose on the persistent GameManager and destroys it on teardown;
    /// the destroyed-owner prune then releases anything left held.
    /// </summary>
    public sealed class ToolkitLockOwner : MonoBehaviour { }
}
