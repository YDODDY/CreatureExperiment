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

            if (held != null)
            {
                var action = held.GetComponent<IHeldPrimaryAction>();
                if (action != null && action.PrimaryPress(ray))
                {
                    if (action.PrimaryActive)
                    {
                        _active = action;
                        _activeItem = held;
                    }
                    return;
                }
                // No action, or it had nothing to do (an egg not aimed at egg storage): eat it if it is edible.
                TryEat(held, fromHand: true); // the interactor's held reference becomes null once the object is destroyed
                return;
            }

            if (Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                TryEat(hit.collider.GetComponentInParent<Interactable>(), fromHand: false);
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
        }
    }
}
