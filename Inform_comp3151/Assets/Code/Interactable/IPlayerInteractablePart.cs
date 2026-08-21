using Inkform.Bus;
using Inkform.Player;
using UnityEngine;

namespace Inkform.Interactable
{
    public interface IPlayerInteractablePart : IInteractablePart
    {
        Transform InteractionPoint { get; }
        float InteractionRange { get; }
        bool RepeatWhileHeld { get; }
        InteractionPrompt GetPrompt(PlayerHandler player);
        bool TryInteract(PlayerHandler player);
    }
}
