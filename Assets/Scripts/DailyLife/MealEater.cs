using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The primary action (Left Click - the input asset's existing "Attack" action, unused elsewhere):
    /// - holding the knife: the knife's own action on what is aimed at within <see cref="reach"/>
    ///   (<see cref="KnifeCoating.Use"/> - take jam / butter, spread it on bread); never eating;
    /// - holding edible food or a meal plate: eat it (held state clears as the object is destroyed);
    /// - hands empty, aiming at edible food / a meal plate within <see cref="reach"/>: eat it. Food served
    ///   on a plate is eaten with its plate.
    /// Anything else (raw egg / bacon, other items, empty space): the click does nothing. Interact (E)
    /// stays the plain physical pickup / place for food. Not a generic tool / combat action.
    /// </summary>
    public class MealEater : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionName = "Attack";
        [SerializeField] private PlayerInteractor interactor;
        [Tooltip("Ray origin - usually the Main Camera.")]
        [SerializeField] private Transform aimSource;
        [SerializeField] private float reach = 1.5f;

        private InputAction _primary;

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
        private void OnDisable() => _primary?.Disable();

        private void Update()
        {
            if (_primary == null || !_primary.WasPressedThisFrame())
                return;

            var held = interactor != null ? interactor.HeldItem : null;
            if (held != null)
            {
                var knife = held.GetComponent<KnifeCoating>();
                if (knife != null)
                {
                    UseKnife(knife);
                    return;
                }
                TryEat(held, fromHand: true); // the interactor's held reference becomes null once the object is destroyed
                return;
            }

            if (aimSource == null)
                return;
            var ray = new Ray(aimSource.position, aimSource.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                TryEat(hit.collider.GetComponentInParent<Interactable>(), fromHand: false);
        }

        private void UseKnife(KnifeCoating knife)
        {
            if (aimSource == null)
                return;
            var ray = new Ray(aimSource.position, aimSource.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                knife.Use(hit.collider);
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
