using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A TV's power / channel state and its screen, driven only by a held <see cref="TVRemote"/> aimed at the
    /// TV (Left Click = power, Interact = next channel). The TV is not a World Use target or a receiver.
    ///
    /// Off shows <see cref="offMaterial"/>; on shows the current channel's material. Materials are swapped on
    /// the screen renderer, never edited (the off material is shared with other screens). Starts off on
    /// channel 1; turning off keeps the channel, and nothing resets it between days.
    ///
    /// It is an <see cref="IFocusTarget"/> only so the remote's control prompt can anchor on it: its own name is
    /// empty and it has no outline, so a plain aim at it shows nothing.
    /// </summary>
    public class TVScreen : MonoBehaviour, IFocusTarget
    {
        public const int ChannelCount = 3;

        [SerializeField] private Renderer screenRenderer;
        [Tooltip("Screen while off. Left empty = the screen renderer's material at load.")]
        [SerializeField] private Material offMaterial;

        [Header("Channels")]
        [SerializeField] private Material channel1Material;
        [SerializeField] private Material channel2Material;
        [SerializeField] private Material channel3Material;
        [SerializeField] private string channel1Name = "뉴스";
        [SerializeField] private string channel2Name = "애니메이션";
        [SerializeField] private string channel3Name = "쇼핑";

        [Header("Remote prompts")]
        [SerializeField] private string offPrompt = "TV · LMB 전원 켜기";
        [SerializeField] private string onPrompt = "TV · LMB 전원 끄기 / E 채널 변경";

        private bool _isOn;
        private int _channel;
        private ObjectiveHUD _hud;

        public bool IsOn => _isOn;
        /// <summary>Current channel, 0-based (kept while off).</summary>
        public int Channel => _channel;
        /// <summary>What a remote aimed at this TV can do right now.</summary>
        public string RemotePrompt => _isOn ? onPrompt : offPrompt;

        string IFocusTarget.FocusName => "";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) { }

        private void Awake()
        {
            if (offMaterial == null && screenRenderer != null)
                offMaterial = screenRenderer.sharedMaterial;
            ApplyScreen();
        }

        public void TogglePower()
        {
            _isOn = !_isOn;
            ApplyScreen();
            if (_isOn)
                ShowChannelNotice();
        }

        /// <summary>Next channel, wrapping around. Does nothing while off.</summary>
        public void NextChannel()
        {
            if (!_isOn)
                return;
            _channel = (_channel + 1) % ChannelCount;
            ApplyScreen();
            ShowChannelNotice();
        }

        private void ApplyScreen()
        {
            if (screenRenderer == null)
                return;
            Material mat = _isOn ? ChannelMaterial(_channel) : offMaterial;
            if (mat != null)
                screenRenderer.sharedMaterial = mat;
        }

        private Material ChannelMaterial(int channel) => channel switch
        {
            0 => channel1Material,
            1 => channel2Material,
            _ => channel3Material,
        };

        private string ChannelName(int channel) => channel switch
        {
            0 => channel1Name,
            1 => channel2Name,
            _ => channel3Name,
        };

        private void ShowChannelNotice()
        {
            if (_hud == null)
                _hud = FindFirstObjectByType<ObjectiveHUD>();
            if (_hud != null)
                _hud.ShowNotice($"CH {_channel + 1} · {ChannelName(_channel)}");
        }
    }
}
