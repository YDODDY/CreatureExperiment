using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One "취급주의" sticker (one use). Carried like any Interactable. Handed to a sealed
    /// <see cref="PackingBox"/> it marks the box fragile; handed to a <see cref="StickerSurface"/>
    /// (a wall / floor / ceiling) it is just stuck there for fun.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class FragileStickerItem : MonoBehaviour
    {
    }
}
