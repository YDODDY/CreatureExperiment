using System;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// What an <see cref="Interactable"/> IS - a thin identity layer for questions that cut across domains
    /// ("is this food or an ingredient?", "is this a store product?"). Several may apply (an egg is Food and
    /// Ingredient; an egg carton is Ingredient, Container and Product).
    ///
    /// Identity only, never capability: what an item can DO (be eaten, cooked, broken, thrown, stuck, refilled,
    /// thrown away...) stays with its components / interfaces / fields. Separate from <see cref="Interactable.ItemId"/>
    /// (which exact kind of item) and from <see cref="Interactable.IsDiscardable"/> (a policy).
    /// </summary>
    [Flags]
    public enum ItemTag
    {
        None = 0,
        /// <summary>Something to eat (even if it has to be cooked first).</summary>
        Food = 1 << 0,
        /// <summary>A cooking / food ingredient, loose or packaged.</summary>
        Ingredient = 1 << 1,
        /// <summary>Something the player uses as a tool.</summary>
        Tool = 1 << 2,
        /// <summary>Kitchen utensils and dishes.</summary>
        Kitchenware = 1 << 3,
        /// <summary>Holds other contents / portions.</summary>
        Container = 1 << 4,
        /// <summary>A packaged goods unit, the kind a store stocks and sells as one.</summary>
        Product = 1 << 5,
        /// <summary>Belongs to the workplace gameplay domain.</summary>
        WorkItem = 1 << 6,
        /// <summary>An ordinary physical world object with no higher domain identity.</summary>
        Prop = 1 << 7,
    }
}
