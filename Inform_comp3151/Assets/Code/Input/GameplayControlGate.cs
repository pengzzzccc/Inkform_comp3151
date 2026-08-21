using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Input
{
    /// <summary>Source-aware gameplay lock. Interact remains available so modal world views can close.</summary>
    public static class GameplayControlGate
    {
        public static event Action<bool> Changed;
        private static readonly HashSet<object> sources = new HashSet<object>();

        public static bool IsBlocked => sources.Count > 0;

        public static void Acquire(object source)
        {
            if (source == null) return;
            bool wasBlocked = IsBlocked;
            sources.Add(source);
            if (wasBlocked != IsBlocked) Changed?.Invoke(IsBlocked);
        }

        public static void Release(object source)
        {
            if (source == null) return;
            bool wasBlocked = IsBlocked;
            sources.Remove(source);
            if (wasBlocked != IsBlocked) Changed?.Invoke(IsBlocked);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            sources.Clear();
        }
    }
}
