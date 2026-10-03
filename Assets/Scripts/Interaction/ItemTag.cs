using System;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// What an <see cref="Interactable"/> IS - a thin identity layer for questions that cut across domains
    /// ("is this food or an ingredient?", "is this a drink?"). Several may apply (an egg is Food and Ingredient;
    /// an egg carton is Ingredient and Container).
    ///
    /// Item Architecture v1 rules (Reports/ItemTemplate_v1.md):
    /// - Identity only, never capability: what an item can DO (be eaten, cooked, broken, thrown, stuck, refilled,
    ///   thrown away...) stays with its components / interfaces / fields.
    /// - Never physical: how hard / sharp it is for the things it hits is <see cref="ImpactClass"/>.
    /// - Never commerce: whether a store sells it is <c>StoreProduct</c> alone.
    /// - Bit values are serialized - never renumber; new tags take the next free bit.
    /// Separate from <see cref="Interactable.ItemId"/> (which exact kind of item) and from
    /// <see cref="Interactable.IsDiscardable"/> (a policy).
    /// </summary>
    [Flags]
    public enum ItemTag
    {
        None = 0,
        /// <summary>Something to eat (even if it has to be cooked first).</summary>
        Food = 1 << 0,
        /// <summary>A cooking / food ingredient, loose or packaged.</summary>
        Ingredient = 1 << 1,
        /// <summary>Something the player holds and uses on something else (knife, pan, tape, torch, remote).</summary>
        Tool = 1 << 2,
        /// <summary>Kitchen utensils and dishes (pan, pot, knife, plate, bowl, cup, board).</summary>
        Kitchenware = 1 << 3,
        /// <summary>A package / holder whose point is what is inside it (egg carton, packs, jars). Not a drink - see <see cref="Drink"/>.</summary>
        Container = 1 << 4,
        /// <summary>Deprecated in v1 - a store sells it = <c>StoreProduct</c>. Kept only so the bit is never reused.</summary>
        [Obsolete("Item Architecture v1: whether a store sells an item is StoreProduct, not a tag.")]
        Product = 1 << 5,
        /// <summary>Belongs to the workplace gameplay domain (work item, packing box, tape, fragile sticker).</summary>
        WorkItem = 1 << 6,
        /// <summary>An ordinary physical object with no higher domain identity (empty can, cigarette, keepsake).</summary>
        Prop = 1 << 7,
        /// <summary>Something to drink (can, bottle, café cup) while it still has its drink in it.</summary>
        Drink = 1 << 8,
    }
}
