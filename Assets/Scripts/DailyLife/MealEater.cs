using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The primary action (Left Click - the input asset's "Attack" action): "use what is in the hand".
    /// 1. The held item has its own action (<see cref="IHeldPrimaryAction"/> - knife, fragile sticker, tape
    ///    roll): the press goes to it with the camera centre ray. If it starts a press / hold / release
    ///    action (drawing tape) this keeps feeding it the ray while the button is down, releases it on
    ///    button up, and cancels it if the item leaves the hand or this component is switched off
    ///    (<see cref="PlayerControlLock"/>).
    /// 2. Otherwise (or if that action reports it had nothing to do) a held edible food / meal plate is eaten.
    /// 3. Hands empty, aiming at edible food / a meal plate within <see cref="reach"/>: eat it. Food served
    ///    on a plate is eaten with its plate.
    /// Before 2 / 3: aiming at a container lying in the world (egg carton, bacon pack, bread bag, cigarette pack -
    /// <see cref="IAimedPrimaryAction"/>) takes one of its contents into a free slot; otherwise, if the held item is
    /// such a container itself, one of ITS contents goes into a free slot and the container stays in the hand.
    /// Order: held item's own action (carton → fridge holder) > aimed world container > held container > eat.
    /// Anything else: nothing. Interact (E) stays the plain world interaction (pickup / place / use).
    /// </summary>
    public class MealEater : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionName = "Attack";
        [SerializeField] private PlayerInteractor interactor;
        [Tooltip("Ray origin - usually the Main Camera.")]
        [SerializeField] private Transform aimSource;
        [Tooltip("Reach for eating aimed food with empty hands.")]
        [SerializeField] private float reach = 1.5f;
        [Tooltip("Reach of a fixed world action that outranks the hand (a hideable locker) - matches the World Use range its label is shown at.")]
        [SerializeField] private float overrideReach = 2.2f;

        private InputAction _primary;
        private IHeldPrimaryAction _active;
        private Interactable _activeItem;

        private void Awake()
        {
            if (inputActions != null)
            {
                var map = inputActions.FindActionMap("Player", throwIfNotFound: false);
                _primary = map?.FindAction(actionName, throwIfNotFound: false);
            }
            if (interactor == null)
                interactor = GetComponent<PlayerInteractor>();
            if (aimSource == null && Camera.main != null)
                aimSource = Camera.main.transform;
        }

        private void OnEnable() => _primary?.Enable();

        private void OnDisable()
        {
            CancelActive();
            _primary?.Disable();
        }

        private void Update()
        {
            if (_primary == null || aimSource == null)
                return;

            var held = interactor != null ? interactor.HeldItem : null;
            var ray = new Ray(aimSource.position, aimSource.forward);

            // A press / hold / release action in progress (tape being drawn).
            if (_active != null)
            {
                if (held == null || held != _activeItem || _active as Object == null)
                    CancelActive();
                else if (_primary.IsPressed())
                    _active.PrimaryHold(ray);
                else
                {
                    _active.PrimaryRelease(ray);
                    _active = null;
                    _activeItem = null;
                }
                return;
            }

            if (!_primary.WasPressedThisFrame())
                return;

            // A fixed world action that outranks the hand (a locker to hide in): before any held-item use, with the same
            // reach as the World Use label that announces it.
            if (TryAimedOverride(ray))
                return;

            if (held != null)
            {
                // Every Left Click action on the item, in component order, until one uses the press (a raw egg: into the
                // pan it is aimed at (FoodItem), else into the fridge holder it is aimed at (StockRefiller), else on below).
                foreach (var action in held.GetComponents<IHeldPrimaryAction>())
                {
                    if (!action.PrimaryPress(ray))
                        continue;
                    if (action.PrimaryActive)
                    {
                        _active = action;
                        _activeItem = held;
                    }
                    return;
                }
                // No action, or it had nothing to do: a container aimed at in the world gives one of its contents,
                // otherwise eat what is in the hand if it is edible.
                if (TryTakeFromAimedContainer(ray))
                    return;
                // The held item is itself a container (a cigarette pack, an egg carton): take one out into a free slot.
                IAimedPrimaryAction heldTake = AimedPrimary.Find(held);
                if (heldTake != null && heldTake.TryAimedPrimary())
                    return;
                TryEat(held, fromHand: true); // the interactor's held reference becomes null once the object is destroyed
                return;
            }

            if (TryTakeFromAimedContainer(ray))
                return;
            // Food seated in a pan resolves to the pan (not edible): hot food is served onto a plate first, never eaten off the pan.
            if (Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                TryEat(Interactable.PickupTarget(hit.collider.GetComponentInParent<Interactable>()), fromHand: false);
        }

        private bool TryAimedOverride(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(reach, overrideReach), ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.GetComponentInParent<Interactable>() != null)
                return false;
            var fixedAction = hit.collider.GetComponentInParent<IAimedPrimaryAction>();
            if (fixedAction == null || !fixedAction.OverridesHeldItem)
                return false;
            // Aimed at it = the press belongs to it, even when it can't act right now (a locker door still swinging):
            // never falls through to eating / drinking what is in the hand.
            if (fixedAction.PrimaryEnabled)
                fixedAction.TryAimedPrimary();
            return true;
        }

        // Left Click on a container in the world (egg carton, pack - IAimedPrimaryAction on a pickup), or on a fixed one that is
        // not a pickup at all (the fridge door egg holder): take one of its contents.
        private bool TryTakeFromAimedContainer(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                return false;
            var item = Interactable.PickupTarget(hit.collider.GetComponentInParent<Interactable>());
            if (item == null)
            {
                var fixedTake = hit.collider.GetComponentInParent<IAimedPrimaryAction>();
                return fixedTake != null && fixedTake.PrimaryEnabled && fixedTake.TryAimedPrimary();
            }
            if (item.IsHeld)
                return false;
            IAimedPrimaryAction take = AimedPrimary.Find(item);
            return take != null && take.TryAimedPrimary();
        }

        private void CancelActive()
        {
            if (_active != null && _active as Object != null)
                _active.PrimaryCancel();
            _active = null;
            _activeItem = null;
        }

        // From the world only what nobody is carrying (a creature may hold it).
        private static void TryEat(Interactable target, bool fromHand)
        {
            if (target == null)
                return;

            var food = target.GetComponent<FoodItem>();
            if (food != null && food.Plate != null)
            {
                target = food.Plate.Interactable;
                food = null;
            }

            if (!fromHand && target.IsHeld)
                return;

            var meal = target.GetComponent<MealPlate>();
            if (meal != null && meal.IsEdible)
                meal.Eat();
            else if (food != null && food.IsEdible)
                food.Eat();
            else if (fromHand && food != null)
                ObjectiveHUD.Notice(food.NotReadyNotice); // raw: "조리가 필요해." / "구워서 먹어야 해." / "냄비에 넣어야 해."
            else if (fromHand && meal != null)
                ObjectiveHUD.Notice("비어 있어.");
            else if (fromHand && target.TryGetComponent(out DrinkContainer drink) && !drink.IsFull)
                ObjectiveHUD.Notice("이미 다 마셨어."); // an empty can / bottle: the same object, nothing left in it
        }
    }
}
