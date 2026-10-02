using System;
using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    public enum ConsumeKind { Food, Drink }

    /// <summary>
    /// One place to hear "the player actually ate / drank something", whatever it was: raised by the existing consume
    /// points at the moment the consumption really happens - <see cref="FoodItem.Eat"/>, <see cref="MealPlate.Eat"/>,
    /// <see cref="ReadyToEatFood"/> (the bite) as <see cref="ConsumeKind.Food"/>; <see cref="DrinkContainer"/> (a full drink
    /// emptied by drinking, not by a burst) as <see cref="ConsumeKind.Drink"/>. A refused or cancelled attempt raises
    /// nothing. The object is still alive during the call.
    /// </summary>
    public static class ConsumeEvents
    {
        public static event Action<GameObject, ConsumeKind> Consumed;

        public static void Raise(GameObject consumed, ConsumeKind kind) => Consumed?.Invoke(consumed, kind);
    }
}
