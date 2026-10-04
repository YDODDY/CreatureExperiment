using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>One seat of a <see cref="SeesawRide"/> (on the seat's collider): E sits the player down on that side.</summary>
    public class SeesawSeat : MonoBehaviour, IUsable, IFocusTarget
    {
        [SerializeField] private SeesawRide ride;
        [Tooltip("SeesawRide.SideA (0) or SideB (1).")]
        [SerializeField] private int side;

        public bool CanUse => ride != null && ride.CanSeatPlayer(side);
        public void Use() => ride?.SeatPlayer(side);

        public string FocusName => CanUse ? ride.SitPrompt : "";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) { }
    }
}
