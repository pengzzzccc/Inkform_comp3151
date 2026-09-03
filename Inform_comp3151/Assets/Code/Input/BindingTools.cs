using System;
using Inkform.Settings;
using UnityEngine.InputSystem;

namespace Inkform.Input
{
    /// <summary>
    /// Runtime binding-edit helpers for the Controls tab. The panel only knows "this row is action X,
    /// composite part Y, on device group Z" — the InputSystem mechanics (binding-index lookup, display
    /// strings, the interactive rebind operation, persisting overrides) live here in the Input domain.
    ///
    /// Overrides are written to the shared InputActions asset and stored as JSON in SettingsStore,
    /// which InputActions reloads at startup — so a remap survives play-mode restarts.
    /// </summary>
    public static class BindingTools
    {
        /// <summary>InputAction binding group names from the input asset (see InputSystem_Actions.cs).</summary>
        public const string KbmGroup = "Keyboard&Mouse";
        public const string GamepadGroup = "Gamepad";

        /// <summary>
        /// Finds the binding index of an action's first binding in a device group. compositePart
        /// selects one part of a composite (Move's "up"/"down"/"left"/"right"); null/empty picks the
        /// first plain (non-composite) binding. Returns -1 when nothing matches.
        /// </summary>
        public static int FindBindingIndex(InputAction action, string group, string compositePart)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (!MatchesGroup(b.groups, group)) continue;

                if (string.IsNullOrEmpty(compositePart))
                {
                    if (!b.isComposite && !b.isPartOfComposite) return i;
                }
                else if (b.isPartOfComposite && string.Equals(b.name, compositePart, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Human-readable binding string for a row ("Space", "Left Shift", "—" when missing).</summary>
        public static string GetDisplay(InputAction action, int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) return "—";
            return action.GetBindingDisplayString(bindingIndex);
        }

        /// <summary>
        /// Starts an interactive rebind for one binding. excludeGroup is the opposite device family
        /// (so a KBM row never rebinds to a gamepad button and vice versa). onComplete fires with the
        /// new binding already saved; Esc cancels.
        ///
        /// Known minor quirk: clicking the Rebind button itself can be picked up as a mouse binding if
        /// the target is a mouse button (RopeFire). OnMatchWaitForAnother softens it; the default was
        /// already a mouse button, so the practical harm is nil.
        /// </summary>
        public static void StartRebind(InputAction action, int bindingIndex, string excludeGroup, Action onComplete)
        {
            CancelActive();

            var op = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Keyboard>/escape")     // Esc must stay free to cancel
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);

            if (excludeGroup == GamepadGroup)
                op.WithControlsExcluding("<Gamepad>");
            else if (excludeGroup == KbmGroup)
            {
                // One path per call; chaining excludes the whole keyboard + mouse family
                op.WithControlsExcluding("<Keyboard>");
                op.WithControlsExcluding("<Mouse>");
            }

            activeOp = op;

            op.OnCancel(o => { activeOp = null; o.Dispose(); })
              .OnComplete(o =>
              {
                  activeOp = null;
                  string json = InputActions.Wrapper.asset.SaveBindingOverridesAsJson();
                  SettingsStore.BindingOverridesJson = json;
                  o.Dispose();
                  onComplete?.Invoke();
              })
              .Start();
        }

        /// <summary>Aborts a running rebind (the Settings panel calls this on close).</summary>
        public static void CancelActive()
        {
            if (activeOp == null) return;
            activeOp.Cancel();   // fires OnCancel → disposes and clears the field
        }

        /// <summary>
        /// Restores every default binding: clears the overrides from the shared asset and drops the
        /// persisted JSON. Both halves are required — clearing only the asset would reload the old
        /// overrides next startup; clearing only the JSON would leave the asset's overrides live.
        /// </summary>
        public static void ResetAllBindings()
        {
            InputActions.Wrapper.asset.RemoveAllBindingOverrides();
            SettingsStore.BindingOverridesJson = "";   // empty string → the setter deletes the key
        }

        private static InputActionRebindingExtensions.RebindingOperation activeOp;

        // binding.groups is a ';'-separated list (the asset even writes a stray leading ';'), so match
        // by segment, never by substring.
        private static bool MatchesGroup(string bindingGroups, string group)
        {
            if (string.IsNullOrEmpty(bindingGroups)) return false;
            foreach (string g in bindingGroups.Split(';'))
            {
                if (g == group) return true;
            }
            return false;
        }
    }
}
