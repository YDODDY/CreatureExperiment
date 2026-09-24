using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A roll of packing tape (one use). Carried like any Interactable; handing it to a
    /// <see cref="PackingBox"/> that holds an item seals the box and uses the tape up.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class TapeItem : MonoBehaviour
    {
    }
}
