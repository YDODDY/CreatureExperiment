using UnityEngine;
using UnityEngine.UI;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A small paper shelf label: store brand, product name and price. Nothing is typed in here - the name and price
    /// are read from the <see cref="ItemSupply"/>'s template (<see cref="Interactable.DisplayName"/>,
    /// <see cref="StoreProduct.Price"/>), the same data the inventory HUD and the checkout use, so the tag can never
    /// disagree with what is charged.
    ///
    /// Built at runtime as a World Space canvas (Legacy Text, like the computer screen) on this object: forward points
    /// INTO the label (the shopper looks along it), local X/Y = the label face. No collider and no raycast target, so
    /// it never catches the Interact ray meant for the shelf.
    /// </summary>
    public class PriceTag : MonoBehaviour
    {
        [Tooltip("The shelf supply whose product this tag prices.")]
        [SerializeField] private ItemSupply supply;
        [SerializeField] private string brand = "DAILY MART";
        [Tooltip("Label size in metres (width, height).")]
        [SerializeField] private Vector2 size = new Vector2(0.3f, 0.13f);
        [Tooltip("Canvas units per metre - sets the text sharpness.")]
        [SerializeField] private float unitsPerMeter = 2000f;

        private static readonly Color PaperColor = new Color(0.97f, 0.96f, 0.9f);
        private static readonly Color BrandBarColor = new Color(0.78f, 0.12f, 0.12f);
        private static readonly Color InkColor = new Color(0.1f, 0.1f, 0.1f);

        private Font _font;

        private void Awake()
        {
            GameObject template = supply != null ? supply.Template : null;
            if (template == null || !template.TryGetComponent(out StoreProduct product))
            {
                Debug.LogWarning($"{name}: no ItemSupply with a StoreProduct template - price tag left blank.", this);
                return;
            }
            string productName = template.TryGetComponent(out Interactable item) ? item.DisplayName : template.name;
            Build(productName, PlayerWallet.FormatUsd(product.Price));
        }

        private void Build(string productName, string price)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var root = new GameObject("PriceTagCanvas", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)root.transform;
            rt.SetParent(transform, false);
            rt.sizeDelta = size * unitsPerMeter;
            rt.localScale = Vector3.one / unitsPerMeter;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            // Paper, a red brand strip along the top, the name on the left and the price large on the right.
            float h = rt.sizeDelta.y;
            float bar = h * 0.27f;
            Panel(rt, "Paper", PaperColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var brandBar = Panel(rt, "Brand", BrandBarColor, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -bar), Vector2.zero);
            Label(brandBar, brand, Mathf.RoundToInt(bar * 0.72f), Color.white, TextAnchor.MiddleLeft, FontStyle.Bold, new Vector2(12f, 0f), Vector2.zero);

            var body = Panel(rt, "Body", Color.clear, Vector2.zero, Vector2.one, new Vector2(14f, 6f), new Vector2(-12f, -bar));
            Label(body, productName, Mathf.RoundToInt(h * 0.24f), InkColor, TextAnchor.MiddleLeft, FontStyle.Bold, Vector2.zero, new Vector2(-rt.sizeDelta.x * 0.4f, 0f));
            Label(body, price, Mathf.RoundToInt(h * 0.36f), InkColor, TextAnchor.MiddleRight, FontStyle.Bold, new Vector2(rt.sizeDelta.x * 0.45f, 0f), Vector2.zero);
        }

        private static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            if (color.a > 0f)
            {
                var img = go.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }
            return rt;
        }

        private void Label(Transform parent, string text, int fontSize, Color color, TextAnchor alignment, FontStyle style, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rt = Panel(parent, "Text", Color.clear, Vector2.zero, Vector2.one, offsetMin, offsetMax);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            t.alignment = alignment;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.97f, 0.96f, 0.9f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.002f));
        }
    }
}
