using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// "Is this food?" from the components an item already has - never from names. Food: something eaten
    /// (<see cref="ReadyToEatFood"/> - chips, choco bar, pastry, sandwich, cup noodle, croissant; <see cref="FoodItem"/>;
    /// <see cref="MealPlate"/>) or a package whose contents are such food (a <see cref="ConsumableStock"/> handing out a
    /// <see cref="FoodItem"/> / <see cref="ReadyToEatFood"/> - egg carton, bacon pack, bread bag). Not food: drinks
    /// (<see cref="DrinkContainer"/>, beer), cigarettes and their pack, tools, household goods.
    /// </summary>
    public static class FoodClassification
    {
        public static bool IsFood(GameObject item)
        {
            if (item == null)
                return false;
            if (item.GetComponent<ReadyToEatFood>() != null || item.GetComponent<FoodItem>() != null || item.GetComponent<MealPlate>() != null)
                return true;
            if (item.GetComponent<ConsumableStock>() == null)
                return false;
            foreach (var dispenser in item.GetComponentsInChildren<PortionDispenser>(true))
            {
                GameObject content = dispenser.Template;
                if (content != null && (content.GetComponent<FoodItem>() != null || content.GetComponent<ReadyToEatFood>() != null))
                    return true;
            }
            return false;
        }
    }
}
