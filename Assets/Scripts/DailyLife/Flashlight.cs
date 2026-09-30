using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A hand torch. An ordinary Interactable (inventory slot, E / Place / F / Right Click / trash); Left Click while it
    /// is in the hand (<see cref="IHeldPrimaryAction"/>, routed by <c>MealEater</c>) switches the beam on / off. The
    /// beam is a spot <see cref="Light"/> child pointing along the torch's own +Z, so it lights wherever the torch
    /// points - in the hand, lying on a table, or tumbling after a throw (a torch put down or thrown while on stays on).
    ///
    /// Put away into another inventory slot (or hidden while the player sits at the computer) it switches off: the
    /// inventory hides a stashed item's renderers, and a hidden <see cref="body"/> while held means "not in the hand".
    /// Taken out again it starts off. Unpaid, the switch refuses like any store product
    /// (<see cref="StoreProduct.RefuseUnpaidUse"/>).
    ///
    /// No battery, no flicker - later additions belong here (e.g. a charge that <see cref="SetOn"/> checks).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class Flashlight : MonoBehaviour, IHeldPrimaryAction
    {
        [SerializeField] private bool on;
        [Tooltip("Spot light at the head, facing the torch's +Z.")]
        [SerializeField] private Light beam;
        [Tooltip("Optional lit-lens look, shown only while on.")]
        [SerializeField] private GameObject lensGlow;
        [Tooltip("Main body renderer - hidden while held means the torch was put away into another slot.")]
        [SerializeField] private Renderer body;

        private Interactable _item;

        public bool IsOn => on;

        private void Awake()
        {
            _item = GetComponent<Interactable>();
            Apply();
        }

        public bool PrimaryPress(Ray aim)
        {
            if (StoreProduct.RefuseUnpaidUse(this))
                return true;
            SetOn(!on);
            return true;
        }

        public void SetOn(bool value)
        {
            on = value;
            Apply();
        }

        private void LateUpdate()
        {
            if (on && _item.IsHeld && body != null && !body.enabled)
                SetOn(false);
        }

        private void Apply()
        {
            if (beam != null) beam.enabled = on;
            if (lensGlow != null) lensGlow.SetActive(on);
        }
    }
}
