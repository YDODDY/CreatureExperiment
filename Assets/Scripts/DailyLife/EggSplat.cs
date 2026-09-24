using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// What a broken raw egg leaves: a flat stain (primitives, no collider) lying on the surface - local up
    /// is the surface normal - and a very short burst of white / yellow bits flying out and shrinking away.
    /// The stain stays for the session (no save). Spawned by <see cref="RawEggBreak"/>.
    /// </summary>
    public class EggSplat : MonoBehaviour
    {
        [SerializeField] private GameObject stain;
        [Tooltip("Parent of the burst bits; each flies out along its own local offset.")]
        [SerializeField] private Transform burst;
        [SerializeField] private float burstDuration = 0.25f;
        [SerializeField] private float burstSpread = 0.14f;

        private Vector3[] _startPos;
        private Vector3[] _startScale;
        private float _t = -1f;
        private bool _keep = true;

        public void Play(bool leaveStain)
        {
            _keep = leaveStain;
            if (stain != null)
                stain.SetActive(leaveStain);
            if (burst == null)
                return;

            int n = burst.childCount;
            _startPos = new Vector3[n];
            _startScale = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Transform bit = burst.GetChild(i);
                _startPos[i] = bit.localPosition;
                _startScale[i] = bit.localScale;
            }
            burst.gameObject.SetActive(true);
            _t = 0f;
        }

        private void Update()
        {
            if (_t < 0f)
                return;

            _t += Time.deltaTime;
            float k = burstDuration > 0f ? Mathf.Clamp01(_t / burstDuration) : 1f;
            for (int i = 0; i < _startPos.Length; i++)
            {
                Transform bit = burst.GetChild(i);
                Vector3 outward = _startPos[i].sqrMagnitude > 0f ? _startPos[i].normalized : Vector3.up;
                bit.localPosition = _startPos[i] + outward * (burstSpread * k);
                bit.localScale = _startScale[i] * (1f - k);
            }
            if (k < 1f)
                return;

            _t = -1f;
            burst.gameObject.SetActive(false);
            if (!_keep)
                Destroy(gameObject);
        }
    }
}
