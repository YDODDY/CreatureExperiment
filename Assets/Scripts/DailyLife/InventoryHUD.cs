using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The four inventory slots along the bottom of the screen (number, item name, active slot framed) and the
    /// wallet balance above them (USD). A store product not yet paid for shows its price in red with "미결제".
    /// Reads <see cref="PlayerInteractor"/> / <see cref="PlayerWallet"/> only; OnGUI like the other HUDs.
    /// </summary>
    public class InventoryHUD : MonoBehaviour
    {
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor interactor;
        [Tooltip("Found on the interactor if empty.")]
        [SerializeField] private PlayerWallet wallet;
        [SerializeField] private float slotSize = 76f;
        [SerializeField] private float gap = 6f;
        [SerializeField] private float bottomMargin = 18f;
        [SerializeField] private int fontSize = 13;

        private GUIStyle _name, _small, _money, _hint;
        private Texture2D _white;

        private void Awake()
        {
            if (interactor == null)
                interactor = FindFirstObjectByType<PlayerInteractor>();
            if (wallet == null && interactor != null)
                wallet = interactor.GetComponent<PlayerWallet>();
        }

        private void OnGUI()
        {
            if (interactor == null || interactor.SlotCount == 0)
                return;
            EnsureStyles();

            int n = interactor.SlotCount;
            float width = n * slotSize + (n - 1) * gap;
            float x0 = (Screen.width - width) * 0.5f;
            float y = Screen.height - bottomMargin - slotSize;

            for (int i = 0; i < n; i++)
            {
                var r = new Rect(x0 + i * (slotSize + gap), y, slotSize, slotSize);
                bool active = i == interactor.ActiveSlot;
                Fill(r, new Color(0f, 0f, 0f, active ? 0.6f : 0.4f));
                if (active)
                    Frame(r, 2f, Color.white);

                GUI.Label(new Rect(r.x + 5f, r.y + 2f, 20f, 18f), (i + 1).ToString(), _small);

                Interactable item = interactor.GetSlotItem(i);
                if (item == null)
                    continue;
                GUI.Label(new Rect(r.x + 4f, r.y + 18f, r.width - 8f, 34f), item.DisplayName, _name);
                if (item.TryGetComponent(out StoreProduct product) && !product.IsPaid)
                {
                    Color prev = GUI.color;
                    GUI.color = new Color(1f, 0.5f, 0.45f);
                    GUI.Label(new Rect(r.x + 4f, r.y + r.height - 22f, r.width - 8f, 18f), $"미결제 {PlayerWallet.FormatUsd(product.Price)}", _small);
                    GUI.color = prev;
                }
            }

            if (wallet != null)
                GUI.Label(new Rect(x0, y - 26f, width, 22f), $"잔액 {PlayerWallet.FormatUsd(wallet.Balance)}", _money);

            // A container in the hand (cigarette pack, egg carton): its Left Click takes one out - say so, with the count.
            IAimedPrimaryAction take = AimedPrimary.Find(interactor.HeldItem);
            string hint = take != null ? take.PrimaryHint : PreparedHint(interactor.HeldItem);
            if (!string.IsNullOrEmpty(hint))
            {
                var r = new Rect(x0 + interactor.ActiveSlot * (slotSize + gap) - 40f, y - 50f, slotSize + 80f, 22f);
                GUI.Label(r, hint, _hint);
            }
        }

        // A hot-water item in the hand, prepared and still full: what Left Click does with it now.
        private static string PreparedHint(Interactable held)
        {
            if (held == null || !held.TryGetComponent(out HotWaterPreparable prep) || !prep.IsPrepared)
                return null;
            if (held.TryGetComponent(out DrinkContainer drink))
                return drink.HasDrink ? "LMB 마시기" : null;
            if (held.TryGetComponent(out SpillableContents contents))
                return contents.HasContents ? "LMB 먹기" : null;
            return null;
        }

        private void EnsureStyles()
        {
            if (_name != null)
                return;
            _name = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _name.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = fontSize - 2, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _small.normal.textColor = Color.white;
            _money = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 3, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            _money.normal.textColor = Color.white;
            _hint = new GUIStyle(GUI.skin.label) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _hint.normal.textColor = new Color(1f, 0.92f, 0.6f);
            _white = Texture2D.whiteTexture;
        }

        private void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        private void Frame(Rect r, float t, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
    }
}
