using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The wall card reader by the workplace entrance. One terminal handles both clock-in and
    /// clock-out; it just forwards the interaction and lets <see cref="DailyLifeDirector"/> decide
    /// which (if either) applies right now. Not a pickup - no <c>Interactable</c>.
    /// </summary>
    public class CardTerminal : MonoBehaviour, IUsable
    {
        [SerializeField] private DailyLifeDirector director;

        public void Use()
        {
            if (director != null)
                director.UseTerminal();
        }
    }
}
