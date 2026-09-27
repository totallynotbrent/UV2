using TMPro;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Concert;
using UV2.Data;

namespace UV2.App
{
    // scene bootstrappers: the concert window is the whole app, built at runtime.
    public static class scene_bootstrap
    {
        public static void build_concert_scene()
        {
            try
            {
                var cam_go = new GameObject("main_camera", typeof(Camera));
                var cam = cam_go.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.05f, 0.07f, 1f);
                cam.orthographic = true;
                cam.nearClipPlane = -10f;
                cam.farClipPlane = 100f;

                var host = new GameObject("concert_host");
                host.AddComponent<UV2.UI.concert_window>().open();
            }
            catch (Exception e)
            {
                Debug.LogError($"[boot] concert scene failed: {e}");
                build_error_screen(e);
            }
        }

        // any unexpected boot failure shows itself instead of a blank window.
        private static void build_error_screen(Exception e)
        {
            build_centered_screen($"failed to start:\n{e.GetType().Name}: {e.Message}");
        }

        private static void build_centered_screen(string message)
        {
            var canvas = UV2.UI.ui_theme.build_canvas("error_canvas");
            var root = canvas.transform;
            UV2.UI.ui_theme.panel(root, "backdrop", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0.05f, 0.05f, 0.07f, 1f), raycast: true);

            var txt = UV2.UI.ui_theme.make_text(root, "error", message, 22, UV2.UI.ui_theme.text_main);
            var rect = txt.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760, 0);
            rect.anchoredPosition = Vector2.zero;
            txt.enableWordWrapping = true;
            txt.alignment = TextAlignmentOptions.Center;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
        }
    }
}
