using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A TV remote: an ordinary pick-up-able item that works a <see cref="TVScreen"/> while held and aimed at it -
    /// Left Click toggles power (<see cref="IHeldPrimaryAction"/>), Interact changes channel
    /// (<see cref="IHeldInteractAction"/>; the press is used even while the TV is off, so it never drops the
    /// remote there). Both use the same aim test (<see cref="TryGetAimedTV"/>): the camera centre ray, triggers
    /// ignored, within <see cref="range"/>, and the first thing it hits must belong to the TV. Aimed anywhere
    /// else the remote is just a held item (Left Click does nothing, E runs the usual chain).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class TVRemote : MonoBehaviour, IHeldPrimaryAction, IHeldInteractAction
    {
        [Tooltip("How far the remote reaches a TV (m) - its own reach, longer than pickup / use.")]
        [SerializeField] private float range = 5f;

        /// <summary>The TV the ray's first hit belongs to, if within reach.</summary>
        public bool TryGetAimedTV(Ray aim, out TVScreen tv)
        {
            tv = null;
            if (!Physics.Raycast(aim, out RaycastHit hit, range, ~0, QueryTriggerInteraction.Ignore))
                return false;
            tv = hit.collider.GetComponentInParent<TVScreen>();
            return tv != null;
        }

        public bool PrimaryPress(Ray aim)
        {
            if (!TryGetAimedTV(aim, out TVScreen tv))
                return false;
            tv.TogglePower();
            return true;
        }

        public bool TryInteract(Ray aim)
        {
            if (!TryGetAimedTV(aim, out TVScreen tv))
                return false;
            tv.NextChannel(); // off: nothing changes, but the press is still used
            return true;
        }

        public IFocusTarget GetAimFocus(Ray aim, out string label)
        {
            label = null;
            if (!TryGetAimedTV(aim, out TVScreen tv))
                return null;
            label = tv.RemotePrompt;
            return tv;
        }
    }
}
