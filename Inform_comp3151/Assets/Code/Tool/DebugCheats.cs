#if UNITY_EDITOR
using Inkform.Item;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Tool
{
    /// <summary>
    /// Editor-only cheat toggles, never compiled into builds: F1 infinite bombs, F2 flight,
    /// F3 invincibility, F5 +1 backpack capacity.
    /// A hidden always-on listener flips the static flags; every gameplay consumer reads a flag
    /// behind its own #if UNITY_EDITOR at one choke point each.
    ///
    /// F1-F3 are toggles; F5 is cumulative — every press adds +1 capacity (mash for more), it has
    /// no off state, the HUD and the save slot just follow the number.
    ///
    /// Every toggle announces itself in the console — a stray cheat left on must never
    /// masquerade as a gameplay bug during testing.
    /// </summary>
    public static class DebugCheats
    {
        public static bool InfiniteBombs { get; private set; }
        public static bool Flight { get; private set; }
        public static bool Invincible { get; private set; }

        private static Driver driver;

        // SubsystemRegistration + explicit reset: with Domain Reload off the flags (and the
        // fake-null listener reference) would leak into the next play session otherwise
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            InfiniteBombs = Flight = Invincible = false;
            if (driver == null) driver = Driver.Create();
        }

        /// <summary>Editor cheats announce themselves in the console so a stray toggle can't
        /// masquerade as a gameplay bug.</summary>
        private sealed class Driver : MonoBehaviour
        {
            public static Driver Create()
            {
                GameObject host = new GameObject("Debug Cheats (editor)") { hideFlags = HideFlags.HideAndDontSave };
                return host.AddComponent<Driver>();
            }

            private void Update()
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard == null) return;

                if (keyboard.f1Key.wasPressedThisFrame)
                {
                    InfiniteBombs = !InfiniteBombs;
                    Debug.Log($"[cheat] infinite bombs {(InfiniteBombs ? "ON" : "off")}");
                }
                if (keyboard.f2Key.wasPressedThisFrame)
                {
                    Flight = !Flight;
                    Debug.Log($"[cheat] flight {(Flight ? "ON" : "off")}");
                }
                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    Invincible = !Invincible;
                    Debug.Log($"[cheat] invincible {(Invincible ? "ON" : "off")}");
                }
                if (keyboard.f5Key.wasPressedThisFrame)
                {
                    int before = InventoryStore.Capacity;
                    InventoryStore.AddCapacityForCheat(1);
                    Debug.Log($"[cheat] inventory capacity {before} -> {InventoryStore.Capacity}");
                }
            }
        }
    }
}
#endif
