using System;
using UnityEngine;

namespace Inkform.Bus
{
    public readonly struct InteractionPrompt
    {
        public readonly string Action;
        public readonly bool Available;
        public readonly string UnavailableReason;

        public InteractionPrompt(string action, bool available, string unavailableReason)
        {
            Action = action ?? string.Empty;
            Available = available;
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        public bool IsVisible => !string.IsNullOrWhiteSpace(Action);
    }

    /// <summary>Current nearby interaction prompt, owned by the live PlayerInteractor.</summary>
    public static class InteractionBus
    {
        public static event Action<InteractionPrompt> Changed;
        public static InteractionPrompt Current { get; private set; }

        public static void Set(InteractionPrompt prompt)
        {
            if (Current.Action == prompt.Action && Current.Available == prompt.Available
                && Current.UnavailableReason == prompt.UnavailableReason) return;
            Current = prompt;
            Changed?.Invoke(Current);
        }

        public static void Clear() => Set(default);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            Current = default;
        }
    }
}
