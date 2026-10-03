using System;
using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Store food that is eaten straight from the hand (chips, a chocolate bar, a pastry, a sandwich): Left Click while
    /// held (<see cref="IHeldPrimaryAction"/>, routed by <c>MealEater</c>) eats it and the object is gone. Otherwise an
    /// ordinary Interactable (E / Place / F / Right Click / trash).
    ///
    /// Eating is shown with the holder's <see cref="HeldItemUseMotion"/>: the food goes up to the mouth, two little bites
    /// (a few crumbs in <see cref="crumbColor"/>), gone. While that runs (~0.5 s) <see cref="PrimaryActive"/> is up, so the
    /// interactor ignores E / F / Right Click / slot keys and a second click does nothing. Cancelled before the bite
    /// (control taken away, item left the hand): nothing is eaten and it drops back to the hold pose. Without a motion
    /// component it is eaten at once, as before.
    ///
    /// Deliberately not a <see cref="FoodItem"/>: it never cooks, never goes into a pan or onto a meal plate - it has no
    /// kitchen life at all. Unpaid, it is a closed package like any store product (<see cref="StoreProduct.RefuseUnpaidUse"/>).
    ///
    /// <see cref="readyToEat"/> off: the press only shows <see cref="notReadyNotice"/>. An instant item with a
    /// <see cref="HotWaterPreparable"/> (cup noodle) is not eaten until a hot water station has prepared it; with
    /// <see cref="SpillableContents"/> eating leaves the empty cup (same object) instead of destroying it, and an empty /
    /// spilled cup answers "다 먹었어." / "다 쏟아져서 먹을 수 없어.".
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class ReadyToEatFood : MonoBehaviour, IHeldPrimaryAction
    {
        /// <summary>Raised when any ready-to-eat food is eaten, just before it is destroyed. Nothing listens yet.</summary>
        public static event Action<ReadyToEatFood> Eaten;

        private static ObjectiveHUD s_hud;

        [SerializeField] private bool readyToEat = true;
        [Tooltip("Shown on Left Click while it can't be eaten yet.")]
        [SerializeField] private string notReadyNotice = "뜨거운 물이 있어야 먹을 수 있습니다.";
        [Tooltip("Left Click on its empty container after eating it (a cup noodle).")]
        [SerializeField] private string finishedNotice = "다 먹었어.";
        [Tooltip("Left Click on its container after the contents spilled.")]
        [SerializeField] private string spilledNotice = "다 쏟아져서 먹을 수 없어.";
        [Tooltip("Colour of the few crumbs at the bite.")]
        [SerializeField] private Color crumbColor = new Color(0.85f, 0.65f, 0.35f, 1f);

        private Interactable _item;
        private HeldItemUseMotion _motion;
        private bool _eating;

        public bool IsReadyToEat => readyToEat;
        public bool PrimaryActive => _eating;

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        public bool PrimaryPress(Ray aim)
        {
            if (_eating)
                return true; // already on its way to the mouth
            if (TryGetComponent(out HotWaterPreparable prep) && !prep.IsPrepared)
                return false; // a dry cup noodle: hot water first (HotWaterPreparable answers the press)
            if (TryGetComponent(out SpillableContents contents) && !contents.HasContents)
            {
                ObjectiveHUD.Notice(contents.Spilled ? spilledNotice : finishedNotice);
                return true; // the empty cup: nothing left to eat
            }
            if (StoreProduct.RefuseUnpaidUse(this))
                return true;
            if (!readyToEat)
            {
                if (s_hud == null)
                    s_hud = FindFirstObjectByType<ObjectiveHUD>();
                if (s_hud != null)
                    s_hud.ShowNotice(notReadyNotice);
                return true;
            }

            _motion = HeldItemUseMotion.For(Item);
            if (_motion != null && _motion.PlayOneShot(Item, HeldUseStyle.Eat, Eat, () => _eating = false))
            {
                _eating = true;
                return true;
            }
            Eat();
            return true;
        }

        public void PrimaryCancel()
        {
            if (!_eating)
                return;
            _eating = false;
            if (_motion != null)
                _motion.Cancel(Item);
        }

        private void Eat()
        {
            Vector3 at = _motion != null ? _motion.MouthPosition : transform.position;
            Camera cam = Camera.main;
            ParticleFx.Burst(at, cam != null ? -cam.transform.up + cam.transform.forward * 0.5f : Vector3.down, crumbColor, 7, 0.018f);
            Eaten?.Invoke(this);
            ConsumeEvents.Raise(gameObject, ConsumeKind.Food);
            // Food in a container (a cup noodle) leaves its empty cup in the hand; anything else is simply gone.
            if (TryGetComponent(out SpillableContents contents))
                contents.EmptyOut(spilled: false);
            else
                Destroy(gameObject); // the interactor's held reference reads as empty once the object is gone
        }
    }
}
