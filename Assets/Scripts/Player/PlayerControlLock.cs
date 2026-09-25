using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.DailyLife;

namespace CreatureExperiment.Player
{
    /// <summary>
    /// Temporarily takes world-gameplay control away from the player by switching off the components that
    /// read gameplay input: movement (WASD / jump / crouch), mouse look, Interact / throws
    /// (<see cref="PlayerInteractor"/>, <see cref="PlayerActivator"/>) and the Left Click primary action
    /// (<see cref="MealEater"/>). Same approach as <c>SittableChair</c> turning PlayerMovement off - nothing
    /// is moved, a held item stays in the hand, the CharacterController is left alone.
    ///
    /// <see cref="Release"/> turns back on only what this lock turned off. Note: switching these off also
    /// disables their shared Input System actions (e.g. Interact) - a caller that still needs one while
    /// locked must enable it itself.
    /// </summary>
    public sealed class PlayerControlLock
    {
        private readonly List<Behaviour> _disabled = new List<Behaviour>();

        public static PlayerControlLock Acquire(GameObject player)
        {
            var handle = new PlayerControlLock();
            if (player == null)
                return handle;
            handle.Disable(player.GetComponent<PlayerMovement>());
            handle.Disable(player.GetComponent<PlayerLook>());
            handle.Disable(player.GetComponent<PlayerInteractor>());
            handle.Disable(player.GetComponent<PlayerActivator>());
            handle.Disable(player.GetComponent<MealEater>());
            return handle;
        }

        public void Release()
        {
            foreach (var b in _disabled)
                if (b != null)
                    b.enabled = true;
            _disabled.Clear();
        }

        private void Disable(Behaviour b)
        {
            if (b == null || !b.enabled)
                return;
            b.enabled = false;
            _disabled.Add(b);
        }
    }
}
