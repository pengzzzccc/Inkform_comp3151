using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>Source-aware transient gameplay guidance. The most recently shown source wins.</summary>
    public static class GuidanceBus
    {
        public static event Action<string> Changed;
        public static string Current { get; private set; } = string.Empty;

        private static readonly Dictionary<object, string> messages = new Dictionary<object, string>();
        private static readonly List<object> order = new List<object>();

        public static void Show(object source, string message)
        {
            if (source == null || string.IsNullOrWhiteSpace(message)) return;
            messages[source] = message;
            order.Remove(source);
            order.Add(source);
            Refresh();
        }

        public static void Clear(object source)
        {
            if (source == null) return;
            messages.Remove(source);
            order.Remove(source);
            Refresh();
        }

        private static void Refresh()
        {
            string next = order.Count > 0 && messages.TryGetValue(order[order.Count - 1], out string value)
                ? value : string.Empty;
            if (next == Current) return;
            Current = next;
            Changed?.Invoke(Current);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            Current = string.Empty;
            messages.Clear();
            order.Clear();
        }
    }
}
