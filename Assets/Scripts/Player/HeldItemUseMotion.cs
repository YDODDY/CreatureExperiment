using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Player
{
    /// <summary>How a held item is brought to the mouth.</summary>
    public enum HeldUseStyle
    {
        /// <summary>Quick lift, two little bites, gone.</summary>
        Eat,
        /// <summary>Lift and tip the top toward the mouth, then back down (the container stays).</summary>
        Drink,
        /// <summary>Held at the lips for as long as the button is down (cigarette).</summary>
        Smoke
    }

    /// <summary>
    /// The "bring it to the mouth" motion for consumables, without a hand model: the held item itself moves from its
    /// hold pose to a pose at <see cref="mouthAnchor"/> (a point just in front of / under the camera) and back. Pure
    /// Transform interpolation in LateUpdate - no Animator.
    ///
    /// The hold pose is never stored here: it is always the item's own <see cref="Interactable.HoldPositionOffset"/> /
    /// <see cref="Interactable.HoldRotationOffset"/> under <see cref="holdAnchor"/> (exactly what the interactor sets on
    /// pickup), so ending or cancelling a motion always lands back on it. The motion lets go by itself the moment the
    /// item is no longer on the hold anchor (thrown, placed, destroyed) and never touches it after that.
    ///
    /// Two kinds, both driven by the item's own Left Click action (which keeps its PrimaryActive up meanwhile, so the
    /// interactor blocks E / F / Right Click / slot keys exactly as it already does for the tape roll):
    /// - one-shot (<see cref="PlayOneShot"/>, eat / drink): up, use (the callback - eat, empty the bottle), down.
    /// - hold (<see cref="BeginHold"/> / <see cref="EndHold"/>, smoke): up while held, down on release.
    /// </summary>
    [DefaultExecutionOrder(100)] // after PlayerInteractor, so the pose written here is the one rendered
    public class HeldItemUseMotion : MonoBehaviour
    {
        [SerializeField] private Transform holdAnchor;
        [Tooltip("Point in front of / below the camera where things are eaten, drunk and smoked (camera-rig child, no collider).")]
        [SerializeField] private Transform mouthAnchor;

        [Header("Eat")]
        [SerializeField] private float eatSeconds = 0.55f;
        [Tooltip("Fraction of the motion at which the food is eaten.")]
        [SerializeField] private float eatUseAt = 0.8f;
        [SerializeField] private Vector3 eatMouthOffset = new Vector3(0f, -0.08f, 0.12f);
        [SerializeField] private Vector3 eatTilt = new Vector3(-15f, 0f, 0f);
        [SerializeField] private float biteBob = 0.018f;

        [Header("Drink")]
        [SerializeField] private float drinkSeconds = 0.8f;
        [SerializeField] private float drinkUseAt = 0.6f;
        [SerializeField] private Vector3 drinkMouthOffset = new Vector3(0.02f, 0.05f, 0.14f);
        [Tooltip("Drinking pose (euler in the hold anchor's space, absolute): top at the mouth, bottom raised toward the view.")]
        [SerializeField] private Vector3 drinkTilt = new Vector3(-110f, 0f, 0f);

        [Header("Smoke")]
        [SerializeField] private float smokeRaiseSeconds = 0.2f;
        [Tooltip("Pivot offset from the mouth point - about half the cigarette's length along its pointing direction, so the filter is at the lips.")]
        [SerializeField] private Vector3 smokeMouthOffset = new Vector3(0.04f, -0.03f, 0.11f);
        [Tooltip("Smoking pose (euler in the hold anchor's space, absolute): pointing forward, a little down and right.")]
        [SerializeField] private Vector3 smokeTilt = new Vector3(18f, 18f, 0f);

        private Interactable _item;
        private HeldUseStyle _style;
        private bool _oneShot;
        private float _time, _duration, _useAt;
        private Action _onUse, _onDone;
        private bool _used;
        private float _blend, _blendTarget;

        /// <summary>World position of the mouth (exhale origin). Falls back to just in front of the camera.</summary>
        public Vector3 MouthPosition
        {
            get
            {
                if (mouthAnchor != null) return mouthAnchor.position;
                Camera cam = Camera.main;
                return cam != null ? cam.transform.position + cam.transform.forward * 0.2f - cam.transform.up * 0.1f : transform.position;
            }
        }

        /// <summary>The use motion of whoever holds <paramref name="item"/> (the player), or null.</summary>
        public static HeldItemUseMotion For(Interactable item)
        {
            return item != null && item.Holder is Component holder ? holder.GetComponent<HeldItemUseMotion>() : null;
        }

        /// <summary>A motion is running for <paramref name="item"/>.</summary>
        public bool IsBusy(Interactable item) => item != null && _item == item;

        /// <summary>Up to the mouth, <paramref name="onUse"/> part way, back down; <paramref name="onDone"/> at the end (or on cancel). False = not in the hand: do it without a motion.</summary>
        public bool PlayOneShot(Interactable item, HeldUseStyle style, Action onUse, Action onDone)
        {
            if (!OnAnchor(item) || _item != null)
                return false;
            _item = item;
            _style = style;
            _oneShot = true;
            _time = 0f;
            _duration = style == HeldUseStyle.Drink ? drinkSeconds : eatSeconds;
            _useAt = style == HeldUseStyle.Drink ? drinkUseAt : eatUseAt;
            _onUse = onUse;
            _onDone = onDone;
            _used = false;
            return true;
        }

        /// <summary>Raise <paramref name="item"/> to the mouth and keep it there until <see cref="EndHold"/>.</summary>
        public void BeginHold(Interactable item, HeldUseStyle style)
        {
            if (!OnAnchor(item))
                return;
            if (_item != null && _item != item)
                Stop(restore: true);
            _item = item;
            _style = style;
            _oneShot = false;
            _blendTarget = 1f;
        }

        /// <summary>Lower <paramref name="item"/> back to its hold pose (smoothly).</summary>
        public void EndHold(Interactable item)
        {
            if (_item == item && !_oneShot)
                _blendTarget = 0f;
        }

        /// <summary>Stop at once and snap back to the hold pose. A one-shot that has not used the item yet never will.</summary>
        public void Cancel(Interactable item)
        {
            if (_item == item)
                Stop(restore: true);
        }

        private bool OnAnchor(Interactable item) => item != null && holdAnchor != null && item.transform.parent == holdAnchor;

        private void LateUpdate()
        {
            if (_item is null)
                return;
            if (_item == null || !OnAnchor(_item) || !_item.IsHeld)
            {
                Stop(restore: false); // destroyed, thrown or placed: not ours to pose any more
                return;
            }

            float bob = 0f;
            float blend;
            if (_oneShot)
            {
                _time += Time.deltaTime;
                float k = _duration > 0f ? Mathf.Clamp01(_time / _duration) : 1f;
                blend = k < 0.3f ? Mathf.SmoothStep(0f, 1f, k / 0.3f)
                      : k > _useAt ? 1f - Mathf.SmoothStep(0f, 1f, (k - _useAt) / Mathf.Max(0.01f, 1f - _useAt))
                      : 1f;
                if (_style == HeldUseStyle.Eat && k > 0.3f && k < _useAt)
                    bob = Mathf.Sin((k - 0.3f) / (_useAt - 0.3f) * Mathf.PI * 4f); // two little bites

                if (!_used && k >= _useAt)
                {
                    _used = true;
                    Action use = _onUse;
                    _onUse = null;
                    use?.Invoke();
                    if (_item == null || !OnAnchor(_item))
                    {
                        Stop(restore: false); // eaten: the object is gone
                        return;
                    }
                }
                if (k >= 1f)
                {
                    Stop(restore: true);
                    return;
                }
            }
            else
            {
                _blend = Mathf.MoveTowards(_blend, _blendTarget, Time.deltaTime / Mathf.Max(0.01f, smokeRaiseSeconds));
                if (_blendTarget <= 0f && _blend <= 0f)
                {
                    Stop(restore: true);
                    return;
                }
                blend = Mathf.SmoothStep(0f, 1f, _blend);
            }
            Apply(blend, bob);
        }

        private void Apply(float blend, float bob)
        {
            Vector3 holdPos = _item.HoldPositionOffset;
            Quaternion holdRot = _item.HoldRotationOffset;

            Vector3 offset, tilt;
            switch (_style)
            {
                case HeldUseStyle.Drink: offset = drinkMouthOffset; tilt = drinkTilt; break;
                case HeldUseStyle.Smoke: offset = smokeMouthOffset; tilt = smokeTilt; break;
                default: offset = eatMouthOffset; tilt = eatTilt; break;
            }
            Vector3 mouth = mouthAnchor != null ? holdAnchor.InverseTransformPoint(mouthAnchor.position) : new Vector3(0f, 0.1f, -0.2f);
            Vector3 mouthPos = mouth + offset + new Vector3(0f, bob * biteBob, 0f);
            // Eating keeps the item's own hold orientation (tilted a little); drinking and smoking have fixed poses so
            // every bottle / cigarette ends up the same way at the mouth whatever its hold rotation is.
            Quaternion mouthRot = _style == HeldUseStyle.Eat
                ? Quaternion.Euler(tilt + new Vector3(bob * 6f, 0f, 0f)) * holdRot
                : Quaternion.Euler(tilt);

            Transform t = _item.transform;
            t.localPosition = Vector3.LerpUnclamped(holdPos, mouthPos, blend);
            t.localRotation = Quaternion.Slerp(holdRot, mouthRot, blend);
        }

        private void Stop(bool restore)
        {
            Interactable item = _item;
            Action done = _onDone;
            _item = null;
            _onUse = null;
            _onDone = null;
            _blend = 0f;
            _blendTarget = 0f;
            if (restore && item != null && OnAnchor(item))
            {
                item.transform.localPosition = item.HoldPositionOffset;
                item.transform.localRotation = item.HoldRotationOffset;
            }
            done?.Invoke();
        }
    }
}
