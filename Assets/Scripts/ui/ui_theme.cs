using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace UV2.UI
{
    // ui factory helpers: canvas, panels, buttons, inputs, scroll lists, runtime cjk font.
    public static class ui_theme
    {
        public static readonly Color panel_bg = new Color(0.09f, 0.09f, 0.11f, 0.96f);
        public static readonly Color panel_bg_alt = new Color(0.12f, 0.12f, 0.15f, 0.96f);
        public static readonly Color row_hover = new Color(0.16f, 0.16f, 0.20f, 1f);
        public static readonly Color row_selected = new Color(0.13f, 0.30f, 0.55f, 1f);
        public static readonly Color accent = new Color(0.30f, 0.62f, 1.00f, 1f);
        public static readonly Color text_main = new Color(0.92f, 0.92f, 0.95f, 1f);
        public static readonly Color text_dim = new Color(0.55f, 0.58f, 0.63f, 1f);

        private static TMP_FontAsset _cjk_font;
        private static bool _font_attempted;

        // runtime-built TMP asset from Noto CJK, same recipe as the original viewer's font swap.
        public static TMP_FontAsset cjk_font()
        {
            if (_cjk_font != null) return _cjk_font;
            if (_font_attempted) return null;
            _font_attempted = true;
            var source = Resources.Load<Font>("Fonts/NotoSansCJKsc-Bold");
            if (source == null)
            {
                Debug.LogError("[ui_theme] NotoSansCJKsc-Bold missing from Resources/Fonts");
                return null;
            }
            _cjk_font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (_cjk_font != null) _cjk_font.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _cjk_font;
        }

        // ugui input needs an event system in the scene; without it no click or scroll ever lands.
        public static Canvas build_canvas(string name)
        {
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es_go = new GameObject("event_system", typeof(EventSystem), typeof(StandaloneInputModule));
                Object.DontDestroyOnLoad(es_go);
            }

            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform panel(Transform parent, string name, Vector2 anchor_min, Vector2 anchor_max, Vector2 offset_min, Vector2 offset_max, Color bg, bool raycast = false)
        {
            var go = new GameObject(name, typeof(Image), typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor_min;
            rect.anchorMax = anchor_max;
            rect.offsetMin = offset_min;
            rect.offsetMax = offset_max;
            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = raycast;
            return rect;
        }

        public static TextMeshProUGUI make_text(Transform parent, string name, string content, float size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var go = new GameObject(name, typeof(TextMeshProUGUI), typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.text = content;
            txt.fontSize = size;
            txt.color = color;
            txt.alignment = align == TextAnchor.UpperLeft ? TextAlignmentOptions.TopLeft
                : align == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center
                : align == TextAnchor.MiddleLeft ? TextAlignmentOptions.MidlineLeft
                : align == TextAnchor.LowerCenter ? TextAlignmentOptions.Bottom
                : TextAlignmentOptions.TopLeft;
            txt.font = cjk_font();
            txt.raycastTarget = false;
            txt.enableWordWrapping = false;
            return txt;
        }

        public static TextMeshProUGUI make_text_stretch(Transform parent, string name, string content, float size, Color color, TextAnchor align)
        {
            var txt = make_text(parent, name, content, size, color, align);
            var rect = txt.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return txt;
        }

        public static Button make_button(Transform parent, string name, string label, Vector2 size, System.Action on_click)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button), typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.18f, 0.20f, 0.24f, 1f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => on_click());
            make_text_stretch(go.transform, "label", label, 18, text_main, TextAnchor.MiddleCenter);
            return btn;
        }

        public static TMP_InputField make_input(Transform parent, string name, Vector2 size, string placeholder)
        {
            var go = new GameObject(name, typeof(Image), typeof(TMP_InputField), typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = new Color(0.14f, 0.14f, 0.17f, 1f);
            var input = go.GetComponent<TMP_InputField>();

            var txt = make_text(go.transform, "text", "", 18, text_main, TextAnchor.MiddleLeft);
            var txt_rect = txt.GetComponent<RectTransform>();
            txt_rect.anchorMin = new Vector2(0.02f, 0);
            txt_rect.anchorMax = new Vector2(0.98f, 1);
            txt_rect.offsetMin = Vector2.zero;
            txt_rect.offsetMax = Vector2.zero;
            txt.raycastTarget = true;
            input.textComponent = txt;

            var ph = make_text(go.transform, "placeholder", placeholder, 18, text_dim, TextAnchor.MiddleLeft);
            var ph_rect = ph.GetComponent<RectTransform>();
            ph_rect.anchorMin = new Vector2(0.02f, 0);
            ph_rect.anchorMax = new Vector2(0.98f, 1);
            ph_rect.offsetMin = Vector2.zero;
            ph_rect.offsetMax = Vector2.zero;
            input.placeholder = ph;
            input.fontAsset = cjk_font();
            return input;
        }

        public static Sprite load_sprite(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Debug.LogError($"[ui_theme] image load failed: {path}");
                Object.Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        // builds a vertical scroll view; returns (root, content) where rows are parented to content.
        public static (RectTransform root, RectTransform content) make_scroll(Transform parent, string name, Vector2 anchor_min, Vector2 anchor_max, Vector2 offset_min, Vector2 offset_max)
        {
            var root = panel(parent, name, anchor_min, anchor_max, offset_min, offset_max, panel_bg, raycast: true);

            var viewport = new GameObject("viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(root, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(2, 2);
            viewport.offsetMax = new Vector2(-2, -2);

            var content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0, 0);

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            return (root, content);
        }
    }
}
