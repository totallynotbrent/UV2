using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UV2.App;
using UV2.Live;

namespace UV2.UI
{
    // the in-concert control window: a strip that appears on concert open
    // with a scrub bar, pause/resume, playback speed, and spring settings.
    // the panel reads the stage loader's song clock; seeks forward to the
    // bound music source so audio and timeline stay in lockstep.
    public class control_panel : MonoBehaviour
    {
        timeline_clock clock;
        Slider progress;
        Slider speed;
        Slider spring;
        Toggle collisions;
        Button pause_button;
        TextMeshProUGUI time_label;
        float clip_length;
        bool dragging;

        // builds the panel over an opened stage loader.
        public void open(UV2.Live.stage_loader loader)
        {
            clock = loader.song_clock;
            var src = loader.music_source;
            clip_length = src != null && src.clip != null ? src.clip.length : 0f;

            var canvas = ui_theme.build_canvas("control_canvas");
            var root = canvas.transform;
            var strip = ui_theme.panel(root, "strip",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -64f), new Vector2(0f, 0f),
                ui_theme.panel_bg, raycast: true);

            // progress bar: the full-width scrub track.
            progress = build_slider(strip, "progress", new Vector2(28f, -38f), 900f,
                _ => dragging = true,
                v => { clock.seek(v); dragging = false; });
            progress.minValue = 0f;
            progress.maxValue = clip_length > 0f ? clip_length : 180f;
            progress.interactable = clip_length > 0f;

            time_label = ui_theme.make_text(strip, "time", "0:00 / 0:00", 15, ui_theme.text_main);
            place(time_label.transform, new Vector2(1f, 0.5f), new Vector2(-28f, -32f));

            pause_button = ui_theme.make_button(strip, "pause", "pause", new Vector2(96f, 34f),
                () =>
                {
                    if (clock.paused) { clock.resume(); set_pause_label("pause"); }
                    else { clock.pause(); set_pause_label("resume"); }
                });
            place(pause_button.transform, new Vector2(0f, 0.5f), new Vector2(28f, -32f));

            speed = build_slider(strip, "speed", new Vector2(150f, -32f), 140f,
                v => { clock.set_timescale(v); },
                _ => { });
            speed.minValue = 0.25f;
            speed.maxValue = 2f;
            speed.value = 1f;

            var speed_label = ui_theme.make_text(strip, "speed_label", "speed", 14, ui_theme.text_dim);
            place(speed_label.transform, new Vector2(0f, 0.5f), new Vector2(300f, -32f));

            spring = build_slider(strip, "spring", new Vector2(430f, -32f), 140f,
                v => apply_spring_rate(v),
                _ => { });
            spring.minValue = 0f;
            spring.maxValue = 2f;
            spring.value = 1f;

            var spring_label = ui_theme.make_text(strip, "spring_label", "spring", 14, ui_theme.text_dim);
            place(spring_label.transform, new Vector2(0f, 0.5f), new Vector2(580f, -32f));

            collisions = build_toggle(strip, "collisions", "collisions", new Vector2(700f, -32f));
            collisions.isOn = physics_director.collisions_enabled;
            collisions.onValueChanged.AddListener(on_collision_toggle);

            DontDestroyOnLoad(gameObject);
            DontDestroyOnLoad(canvas.gameObject);
            trace_log.write("control panel: open");
        }

        void set_pause_label(string label)
        {
            var txt = pause_button.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null) txt.text = label;
        }

        void apply_spring_rate(float v)
        {
            if (physics_director.instance == null) return;
            foreach (var sim in physics_director.instance.all_sims())
                physics_director.instance.set_spring_rate(sim, spring_sim.part.head, v);
        }

        void on_collision_toggle(bool on)
        {
            physics_director.collisions_enabled = on;
            trace_log.write($"control panel: collisions {(on ? "on" : "off")}");
        }

        static void place(Transform t, Vector2 anchor, Vector2 pos)
        {
            var r = (RectTransform)t;
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = pos;
        }

        // a horizontal slider built from primitives: track, fill, handle.
        Slider build_slider(Transform parent, string name, Vector2 pos, float width,
            UnityEngine.Events.UnityAction<float> on_change,
            UnityEngine.Events.UnityAction<float> on_end)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(width, 18f);

            var bg_rect = ui_theme.panel(go.transform, "bg", Vector2.zero, Vector2.one,
                new Vector2(0f, 7f), new Vector2(0f, -7f), new Color(0.16f, 0.16f, 0.20f, 1f), raycast: true);
            // sliders swallow events on the whole rect: shrink to the track.
            bg_rect.anchorMin = new Vector2(0f, 0.35f);
            bg_rect.anchorMax = new Vector2(1f, 0.65f);

            var fill = new GameObject("fill area", typeof(RectTransform));
            fill.transform.SetParent(go.transform, false);
            var fill_area = (RectTransform)fill.transform;
            fill_area.anchorMin = new Vector2(0f, 0.25f);
            fill_area.anchorMax = new Vector2(1f, 0.75f);

            var fill_rect = ui_theme.panel(fill_area, "fill", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, ui_theme.accent);

            var handle_area = new GameObject("handle area", typeof(RectTransform));
            handle_area.transform.SetParent(go.transform, false);
            var h_area = (RectTransform)handle_area.transform;
            h_area.anchorMin = new Vector2(0f, 0f);
            h_area.anchorMax = new Vector2(1f, 1f);

            var handle_rect = ui_theme.panel(h_area, "handle", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(-7f, -7f), new Vector2(7f, 7f), ui_theme.text_main, raycast: true);
            handle_rect.pivot = new Vector2(0.5f, 0.5f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fill_rect;
            slider.handleRect = handle_rect;
            slider.targetGraphic = handle_rect.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.onValueChanged.AddListener(on_change);
            return slider;
        }

        // a checkbox built from primitives: background, check, label.
        Toggle build_toggle(Transform parent, string name, string label, Vector2 pos)
        {
            var go = new GameObject(name, typeof(Toggle));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(120f, 24f);

            var bg = ui_theme.panel(go.transform, "bg", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, -9f), new Vector2(18f, 9f), new Color(0.16f, 0.16f, 0.20f, 1f), raycast: true);
            bg.pivot = new Vector2(0f, 0.5f);

            var check = ui_theme.panel(bg, "check", Vector2.zero, Vector2.one,
                new Vector2(2f, 2f), new Vector2(-2f, -2f), ui_theme.accent);
            check.gameObject.SetActive(false);

            var label_text = ui_theme.make_text(go.transform, "label", label, 14, ui_theme.text_dim);
            var label_rect = (RectTransform)label_text.transform;
            label_rect.anchorMin = label_rect.anchorMax = label_rect.pivot = new Vector2(0f, 0.5f);
            label_rect.anchoredPosition = new Vector2(26f, 0f);

            var toggle = go.GetComponent<Toggle>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.targetGraphic = bg.GetComponent<Image>();
            return toggle;
        }

        void Update()
        {
            if (clock == null) return;
            if (progress != null && !dragging)
                progress.SetValueWithoutNotify(clock.time);
            if (time_label != null)
            {
                float total = clip_length > 0f ? clip_length : 0f;
                time_label.text = $"{fmt(clock.time)} / {fmt(total)}";
            }
        }

        static string fmt(float t)
        {
            int m = Mathf.FloorToInt(t / 60f);
            int s = Mathf.FloorToInt(t % 60f);
            return $"{m}:{s:00}";
        }
    }
}
