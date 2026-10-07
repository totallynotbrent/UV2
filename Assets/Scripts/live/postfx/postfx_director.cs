using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UV2.App;

namespace UV2.Live
{
    // evaluates the authored phase-4 postfx tracks per frame and publishes
    // state for the renderer feature's blit chain: bloom -> the game's own
    // FastBloom pyramid (source/4, per-level threshold fold, soft-add
    // composite), dof/film/fog/fade -> globals + overlay quads.
    public class postfx_director : MonoBehaviour
       {
        public live_worksheet ws;
        public timeline_clock clock;
        public Camera cam;

        // the renderer feature reads these every frame (fog + fade globals).
        [HideInInspector] public fog_key fog_state;
        [HideInInspector] public Color fade_color = Color.clear;
        [HideInInspector] public bool fog_enabled;

        // the three film overlay layers, evaluated from the authored key lists.
        [HideInInspector] public film_state film1_state;
        [HideInInspector] public film_state film2_state;
        [HideInInspector] public film_state film3_state;
        // the color-correction state: null when no track or enable=0.
        [HideInInspector] public cc_state cc;

        // the evaluated dof state, read by the render feature.
        [HideInInspector] public bool dof_enabled;
        [HideInInspector] public float dof_focal_m;
        [HideInInspector] public float dof_focal01;
        [HideInInspector] public float dof_far_blend;
        [HideInInspector] public float dof_blur_spread;
        [HideInInspector] public float dof_foreground_size;
        [HideInInspector] public float dof_smoothness;

        // the evaluated tilt-shift overlay state, read by the render feature.
        [HideInInspector] public bool tilt_enabled;
        [HideInInspector] public int tilt_mode;
        [HideInInspector] public int tilt_pass;
        [HideInInspector] public float tilt_blur_area;
        [HideInInspector] public float tilt_max_blur;
        [HideInInspector] public Vector2 tilt_offset;
        [HideInInspector] public float tilt_roll;

        // one film overlay layer's evaluated state.
        public class film_state
        {
            public int mode;
            public float power;
            public float depth_power;
            public float depth_clip;
            public Vector2 offset_param;
            public Vector4 option_param;
            public Color color0;
            public Color color1;
            public Color color2;
            public Color color3;
            public float roll_angle;
            public Vector2 scale = Vector2.one;
            public int layer_mode;
            public bool inverse;
            public bool valid;
        }

        public void open(live_worksheet worksheet, timeline_clock timeline, Camera target, List<Transform> characters = null)
        {
            ws = worksheet;
            clock = timeline;
            cam = target;
            focus_roots = characters;
            cc_lut = null;
        }

        private void LateUpdate()
        {
            if (ws == null || clock == null) return;
            float t = clock.time;

            eval_bloom(t);
            fog_state = eval_fog(t);
            fog_enabled = fog_state != null;
            fade_color = eval_fade(t);
            film1_state = eval_film(ws.postfx.film1, t);
            film2_state = eval_film(ws.postfx.film2, t);
            film3_state = eval_film(ws.postfx.film3, t);
            cc = eval_cc(t);
            eval_dof(t);
            eval_tilt(t);

            // one heartbeat every ~2s so the bench proves the chain engaged.
            if (trace_postfx_ticks++ % 120 == 0)
            {
                string b = ws.postfx.bloom.Count == 0 ? "none"
                    : (bloom_state_valid ? "on" : "off");
                string f = fog_enabled && fog_state != null ? $"on mode {fog_state.fog_mode}" : "off";
                string fl = film1_state != null && film1_state.valid ? $"film m{film1_state.mode} p{film1_state.power:0.00}" : "film none";
                string d = dof_enabled ? $"dof {dof_focal_m:0.0}m f{dof_focal01:0.00} far{dof_far_blend:0.00}" : "dof off";
                string ts = tilt_enabled ? $"tilt m{tilt_mode} a{tilt_blur_area:0.0} blur{tilt_max_blur:0.0} roll{tilt_roll:0.0}" : "tilt off";
                string gr = cc != null && cc.valid ? $"grade sat{cc.saturation:0.00}" : "grade off";
                trace_log.write($"postfx: bloom {b} fog {f} fade a {fade_color.a:0.00} {fl} {d} {ts} {gr}");
            }
        }
        private int trace_postfx_ticks;

        // bloom: the game's FastBloom pyramid parameters from the authored
        // keys (GAME_BLOOM_PIPELINE_DECODED.md).
        public bloom_key bloom_state;
        [HideInInspector] public bool bloom_state_valid;

        private void eval_bloom(float t)
        {
            if (ws.postfx.bloom.Count == 0)
            {
                bloom_state_valid = false;
                return;
            }
            int i = key_eval.bracket(ws.postfx.bloom, t);
            if (i < 0) { bloom_state_valid = false; return; }
            var cur = ws.postfx.bloom[i];
            var next = i + 1 < ws.postfx.bloom.Count ? ws.postfx.bloom[i + 1] : null;
            float intensity = cur.intensity, blur = cur.blur_size, threshold = cur.threshold;
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                intensity = key_eval.lerp_f(cur.intensity, next.intensity, k);
                blur = key_eval.lerp_f(cur.blur_size, next.blur_size, k);
                threshold = key_eval.lerp_f(cur.threshold, next.threshold, k);
            }
            bloom_state = new bloom_key
            {
                intensity = Mathf.Max(0f, intensity),
                blur_size = Mathf.Max(0f, blur),
                threshold = Mathf.Max(0f, threshold),
                blend_mode = cur.blend_mode,
            };
            bloom_state_valid = bloom_state.intensity > 0f;
        }

        // the color-correction state the feature samples: per-channel curves
        // blended between cur/next keys, baked into a 256x1 LUT.
        public class cc_state
        {
            public AnimationCurve red = new();
            public AnimationCurve green = new();
            public AnimationCurve blue = new();
            public float saturation = 1f;
            public bool valid;
        }

        // the graded LUT (256x1), rebuilt when the blended curves move;
        // the game builds it in ColorCorrectionPass::UpdateTextureParameter
        // (0x7ff8e5101ca0).
        [HideInInspector] public Texture2D cc_lut;
        private int cc_lut_frame = -1;
        private float cc_lut_blend = -1f;

        private cc_state eval_cc(float t)
        {
            var keys = ws.postfx.color_correction;
            if (keys == null || keys.Count == 0) return null;
            int i = key_eval.bracket(keys, t);
            if (i < 0) return null;
            var cur = keys[i];
            if (cur.enable == 0) return null;
            var next = i + 1 < keys.Count ? keys[i + 1] : null;
            var s = new cc_state { saturation = cur.saturation, valid = true };
            s.red = cur.red_curve;
            s.green = cur.green_curve;
            s.blue = cur.blue_curve;
            float blend = 0f;
            if (next != null && next.interpolate_type != 0 && next.enable != 0)
                blend = key_eval.interp(cur, next, t);
            if (blend > 0f && next != null)
            {
                s.red = blend_curves(cur.red_curve, next.red_curve, blend);
                s.green = blend_curves(cur.green_curve, next.green_curve, blend);
                s.blue = blend_curves(cur.blue_curve, next.blue_curve, blend);
                s.saturation = Mathf.Lerp(cur.saturation, next.saturation, blend);
            }
            rebuild_cc_lut(s, i, blend);
            return s;
        }

        // lerp two curves by baking the blended curve's sampled values: unity
        // has no curve-lerp primitive, and 256 samples rebuild cheap.
        private AnimationCurve blend_curves(AnimationCurve a, AnimationCurve b, float t)
        {
            var c = new AnimationCurve();
            for (int i = 0; i <= 8; i++)
            {
                float time = i / 8f;
                c.AddKey(time, Mathf.Lerp(a.Evaluate(time), b.Evaluate(time), t));
            }
            return c;
        }

        private void rebuild_cc_lut(cc_state s, int bracket, float blend)
        {
            if (cc_lut != null && cc_lut_frame == bracket
                && Mathf.Abs(blend - cc_lut_blend) < 0.004f) return;
            if (cc_lut == null)
            {
                cc_lut = new Texture2D(256, 1, TextureFormat.RGBA32, false);
                cc_lut.wrapMode = TextureWrapMode.Clamp;
                cc_lut.filterMode = FilterMode.Bilinear;
            }
            var px = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                float u = i / 255f;
                px[i] = new Color32(
                    (byte)(Mathf.Clamp01(s.red.Evaluate(u)) * 255f),
                    (byte)(Mathf.Clamp01(s.green.Evaluate(u)) * 255f),
                    (byte)(Mathf.Clamp01(s.blue.Evaluate(u)) * 255f),
                    255);
            }
            cc_lut.SetPixels32(px);
            cc_lut.Apply();
            cc_lut_frame = bracket;
            cc_lut_blend = blend;
        }

        // fog: the authored key fields publish straight into the classic
        // GlobalFog globals (the game's pass reuses the Standard Assets math).
        private fog_key eval_fog(float t)
        {
            if (ws.postfx.fog.Count == 0) return null;
            int i = key_eval.bracket(ws.postfx.fog, t);
            if (i < 0) return null;
            var cur = ws.postfx.fog[i];
            var next = i + 1 < ws.postfx.fog.Count ? ws.postfx.fog[i + 1] : null;
            var state = new fog_key
            {
                is_distance = cur.is_distance,
                start_distance = cur.start_distance,
                is_height = cur.is_height,
                height = cur.height,
                height_density = cur.height_density,
                color = cur.color,
                fog_mode = cur.fog_mode,
                exp_density = cur.exp_density,
                start = cur.start,
                end = cur.end,
                use_radial_distance = cur.use_radial_distance,
            };
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                state.exp_density = key_eval.lerp_f(cur.exp_density, next.exp_density, k);
                state.start = key_eval.lerp_f(cur.start, next.start, k);
                state.end = key_eval.lerp_f(cur.end, next.end, k);
                state.color = Color.Lerp(cur.color, next.color, k);
                state.height_density = key_eval.lerp_f(cur.height_density, next.height_density, k);
            }
            return state;
        }

        // film: one of the three overlay layers; the validity gate is the
        // game's (Mul/VignetteLerp/VignetteMul always valid, Monochrome needs
        // color0.a, the rest need power).
        private film_state eval_film(List<film_key> keys, float t)
        {
            if (keys == null || keys.Count == 0) return null;
            int i = key_eval.bracket(keys, t);
            if (i < 0) return null;
            var cur = keys[i];
            var next = i + 1 < keys.Count ? keys[i + 1] : null;
            var s = new film_state
            {
                mode = cur.film_mode,
                power = cur.power,
                depth_power = cur.depth_power,
                depth_clip = cur.depth_clip,
                offset_param = cur.offset_param,
                option_param = cur.option_param,
                color0 = cur.color0,
                color1 = cur.color1,
                color2 = cur.color2,
                color3 = cur.color3,
                roll_angle = cur.roll_angle,
                scale = cur.scale,
                layer_mode = cur.layer_mode,
                // key attribute bit 0x100000 = isUseTexMask (uv2_timeline_
                // attribute_gate_decoded.md): the game's UpdatePostFilm copies
                // it into the layer param and the draw helper publishes it as
                // _PostFilmIsInverseVignette - the shader inverts the mask.
                inverse = (cur.attribute & 0x100000) != 0,
            };
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                s.power = key_eval.lerp_f(cur.power, next.power, k);
                s.depth_power = key_eval.lerp_f(cur.depth_power, next.depth_power, k);
                s.roll_angle = key_eval.lerp_f(cur.roll_angle, next.roll_angle, k);
                s.color0 = Color.Lerp(cur.color0, next.color0, k);
                s.color1 = Color.Lerp(cur.color1, next.color1, k);
                s.color2 = Color.Lerp(cur.color2, next.color2, k);
                s.color3 = Color.Lerp(cur.color3, next.color3, k);
                s.offset_param = Vector2.Lerp(cur.offset_param, next.offset_param, k);
                s.option_param = Vector4.Lerp(cur.option_param, next.option_param, k);
            }
            // the game's validity gate, per mode (ScreenOverlayRender.Parameter
            // .IsValidity 0x7ff8e51300e0, register-proven): mode0 never;
            // modes 1/2/5 valid iff filmPower > 0; 3/4/6 always; 7 iff
            // color0.a > 0. AND the ps scales every layer by _DepthPower =
            // key.depthPower, so a valid layer with depthPower==0 paints
            // exactly zero (uv2_film_mode_bodies_decoded.md §6): 1004's
            // film1 never paints at open (first painting key f2190), 1009's
            // film2 never paints at all (0/381 keys carry depthPower>0).
            if (s.mode == 0) s.valid = false;
            else if (s.mode == 3 || s.mode == 4 || s.mode == 6) s.valid = true;
            else if (s.mode == 7) s.valid = s.color0.a > 0f;
            else s.valid = s.power > 0f;
            return s;
        }

        // dof: the game's PrepareDofParam math — focal01 from the camera's
        // focal transform (type 0, what live uses) normalized by far clip,
        // the curve left linear, far blend = focalSize/farClip*0.5 + focal01.
        private void eval_dof(float t)
        {
            if (ws.postfx.dof.Count == 0 || cam == null) { dof_enabled = false; return; }
            int i = key_eval.bracket(ws.postfx.dof, t);
            if (i < 0) { dof_enabled = false; return; }
            var cur = ws.postfx.dof[i];
            var next = i + 1 < ws.postfx.dof.Count ? ws.postfx.dof[i + 1] : null;

            float focal_size = cur.focal_size;
            float blur = cur.blur_spread;
            float fg = cur.foreground_size;
            float fp = cur.focal_point;
            float smooth = cur.smoothness;
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                focal_size = key_eval.lerp_f(cur.focal_size, next.focal_size, k);
                blur = key_eval.lerp_f(cur.blur_spread, next.blur_spread, k);
                fg = key_eval.lerp_f(cur.foreground_size, next.foreground_size, k);
                fp = key_eval.lerp_f(cur.focal_point, next.focal_point, k);
                smooth = key_eval.lerp_f(cur.smoothness, next.smoothness, k);
            }

            // the authored dofFocalPoint resolves to focal01 via
            // WorldToViewportPoint (uv2_dof_focal_path_decoded.md).
            float near = cam.nearClipPlane;
            float far = cam.farClipPlane;
            float far_near = Mathf.Max(1e-4f, far - near);
            Vector3 world_pos = cam.transform.position + (fp - near) * cam.transform.forward;
            float focal01 = Mathf.Max(0f, cam.WorldToViewportPoint(world_pos).z / far_near);
            dof_enabled = focal_size > 0f && fp > 0f;
            // the coc pass wants eye meters; reconstruct the meters of the
            // resolved focal01 (inverse of WorldToViewportPoint().z/(far-near)).
            dof_focal_m = focal01 * far_near + near;
            dof_focal01 = focal01;
            dof_far_blend = focal_size / far_near * 0.5f + focal01;
            dof_blur_spread = blur;
            dof_foreground_size = fg;
            dof_smoothness = Mathf.Max(0.1f, smooth);
        }

        // the character roots for focal resolution, wired at open.
        private List<Transform> focus_roots;

        // tilt-shift per the game's OnUpdateTiltShift (uv2_tiltshift_decoded.
        // md): scalar fields copy un-lerped, the rest ride the next-key rule;
        // pass index = quality*2 + (mode!=1).
        private void eval_tilt(float t)
        {
            if (ws.postfx.tiltshift.Count == 0) { tilt_enabled = false; return; }
            int i = key_eval.bracket(ws.postfx.tiltshift, t);
            if (i < 0) { tilt_enabled = false; return; }
            var cur = ws.postfx.tiltshift[i];
            var next = i + 1 < ws.postfx.tiltshift.Count ? ws.postfx.tiltshift[i + 1] : null;

            tilt_mode = cur.mode;
            int quality = cur.quality;
            float area = cur.blur_area;
            float max_blur = cur.max_blur_size;
            Vector2 off = cur.offset;
            float roll = cur.roll;
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                area = key_eval.lerp_f(cur.blur_area, next.blur_area, k);
                max_blur = key_eval.lerp_f(cur.max_blur_size, next.max_blur_size, k);
                off = Vector2.Lerp(cur.offset, next.offset, k);
                roll = key_eval.lerp_f(cur.roll, next.roll, k);
            }
            // downsample>0 exports _Blurred to an unidentified consumer;
            // clamp to 0 (all son1004 keys author 0 anyway).
            tilt_blur_area = Mathf.Max(0f, area);
            tilt_max_blur = Mathf.Max(0f, max_blur);
            tilt_offset = off;
            tilt_roll = roll;
            tilt_pass = quality * 2 + (tilt_mode != 1 ? 1 : 0);
            tilt_enabled = tilt_mode > 0 && (tilt_blur_area > 0f || tilt_max_blur > 0f);
        }

        // fade: the authored fadeColor's ALPHA is the quad opacity (1004 runs
        // alpha 0 for the whole song then fades to white alpha 1 at the end).
        private Color eval_fade(float t)
        {
            if (ws.postfx.fade.Count == 0) return Color.clear;
            int i = key_eval.bracket(ws.postfx.fade, t);
            if (i < 0) return Color.clear;
            var cur = ws.postfx.fade[i];
            var next = i + 1 < ws.postfx.fade.Count ? ws.postfx.fade[i + 1] : null;
            Color c = cur.color;
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                c = Color.Lerp(cur.color, next.color, k);
            }
            return c;
        }
    }
}
