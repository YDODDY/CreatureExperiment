using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Gives a non-pickup prop (a card terminal, a bed) the same focus outline + name the player
    /// already gets on pick-up-able objects. Purely the visual side - pressing Interact still runs
    /// through the prop's own <see cref="IUsable"/> path. This is NOT an <c>Interactable</c>; the prop
    /// cannot be grabbed.
    /// </summary>
    public class FocusableProp : MonoBehaviour, IFocusTarget
    {
        [Tooltip("Name shown in the focus label. Falls back to the GameObject name.")]
        [SerializeField] private string focusName;
        [Tooltip("Renderer of the outline child - enabled only while focused. Same setup as Interactable's outline.")]
        [SerializeField] private Renderer outlineRenderer;

        public string FocusName => string.IsNullOrEmpty(focusName) ? gameObject.name : focusName;
        public Transform FocusTransform => transform;

        private void Awake()
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }
    }
}
