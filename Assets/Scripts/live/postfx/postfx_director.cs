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
        // the current key's authored chain type (dofBlurType).
        [HideInInspector] public int dof_chain_type;

        // ball blur state (1004/1151 author it; 1001/1032 zero).
        [HideInInspector] public bool ball_blur_enabled;
        [HideInInspector] public float ball_blur_power;
        [HideInInspector] public float ball_blur_threshold;
        [HideInInspector] public float ball_blur_intensity;
        [HideInInspector] public float ball_blur_spread;
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

        // the evaluated radial blur state, read by the render feature. mirrors
        // the game's RadialBlurPass::OnRadialBlur publishes (property ids
        // 212-217 + 188-191, uv2_radialblur_decoded.md).
        [HideInInspector] public bool radial_enabled;
        [HideInInspector] public int radial_type;
        [HideInInspector] public int radial_downsample;
        [HideInInspector] public int radial_iteration;
        [HideInInspector] public Vector4 radial_blur_param;   // _BlurParam: offset.xy, ellipseDir.xy
        [HideInInspector] public Vector4 radial_blur_param_ex; // _BlurParamEx: step, startArea, areaDiff, depthFront
        [HideInInspector] public float radial_end_area;        // normalized depthBack
        [HideInInspector] public bool radial_depth_on;
        [HideInInspector] public bool radial_rect_on;
        [HideInInspector] public Vector4 radial_cancel_rect;  // min.xy, max.xy
        [HideInInspector] public float radial_blend_length;
        // the game's BLURAREADIFF_MIN static (RadialBlurPass constant).
        private const float blur_area_diff_min = 0.001f;

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

            // blink-container coupling authored on the current key.
            public string blink_light_name = "";
            public float blink_light_brightness_power;
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

            // film keys can couple a layer to a named blink container so the
            // stage strobes pulse with the film; the game clears then sets per
            // key (Director's SetFilmCoupling path), strongest authoring wins.
            blink_lights.clear_film_coupling();
            foreach (var fs in new[] { film1_state, film2_state, film3_state })
            {
                if (fs == null) continue;
                if (!string.IsNullOrEmpty(fs.blink_light_name) &&
                    fs.blink_light_brightness_power > 0f)
                    blink_lights.set_film_coupling(fs.blink_light_name, fs.blink_light_brightness_power);
            }
            cc = eval_cc(t);
            eval_dof(t);
            eval_tilt(t);
            eval_radial(t);
            eval_chromatic(t);
            eval_lens_distortion(t);

            // the game's family cousins (Director 1747/1753): an enabled
            // Fluctuation key drives the radial machinery with
            // power=MovePower*4, startArea=0.25; an enabled Vortex key
            // drives the tilt machinery at mode 6, vol=RotVolume*4. the
            // fork's OnUpdateFluctuation/OnUpdateVortex dispatch.
            var fluc = ws.postfx.fluctuation;
            if (fluc != null && fluc.Count > 0)
            {
                int fi = key_eval.bracket(fluc, t);
                if (fi >= 0 && fluc[fi].is_enable != 0)
                {
                    float move_power = fluc[fi].move_power;
                    var fnext = fi + 1 < fluc.Count ? fluc[fi + 1] : null;
                    if (fnext != null && fnext.interpolate_type != 0)
                        move_power = key_eval.lerp_f(fluc[fi].move_power, fnext.move_power,
                            key_eval.interp(fluc[fi], fnext, t));
                    radial_enabled = true;
                    if (radial_type <= 0) radial_type = 1;
                    radial_blur_param = Vector4.zero;
                    radial_blur_param_ex = new Vector4(Mathf.Clamp01(move_power * 4f) * 8f, 0.25f, 0.75f, 0f);
                    radial_downsample = Mathf.Max(1, radial_downsample);
                    trace_log.write($"fluctuation: on frame {(int)(t * 60f)} move{move_power:0.00} -> radial p{radial_blur_param_ex.x:0.00}");
                }
            }
            var vx = ws.postfx.vortex;
            if (vx != null && vx.Count > 0)
            {
                int vi = key_eval.bracket(vx, t);
                if (vi >= 0 && vx[vi].is_enable != 0)
                {
                    float rot = vx[vi].rot_volume;
                    var vnext = vi + 1 < vx.Count ? vx[vi + 1] : null;
                    if (vnext != null && vnext.interpolate_type != 0)
                        rot = key_eval.lerp_f(vx[vi].rot_volume, vnext.rot_volume,
                            key_eval.interp(vx[vi], vnext, t));
                    tilt_enabled = true;
                    tilt_mode = 6;
                    tilt_max_blur = rot * 4f;
                    trace_log.write($"vortex: on frame {(int)(t * 60f)} rot{rot:0.00} -> tilt m6 blur{tilt_max_blur:0.00}");
                }
            }

            // edge-triggered radial trace: one line per enabled/type change so
            // a bench proves engagement even when the throttled heartbeat
            // samples a gap between key windows.
            bool rb_now = radial_enabled;
            if (rb_now != radial_was_on || (rb_now && radial_type != radial_was_type))
            {
                if (rb_now)
                    trace_log.write($"radial: on t{radial_type} frame {(int)(t * 60f)} p{radial_blur_param_ex.x:0.00} it{radial_iteration} ds{radial_downsample} off{radial_blur_param.x:0.00},{radial_blur_param.y:0.00} a{radial_blur_param_ex.y:0.00}");
                else
                    trace_log.write($"radial: off frame {(int)(t * 60f)}");
                radial_was_on = rb_now;
                radial_was_type = radial_type;
            }

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
                string rb = radial_enabled ? $"radial t{radial_type} p{radial_blur_param_ex.x:0.00} it{radial_iteration} ds{radial_downsample}" : "radial off";
                trace_log.write($"postfx: bloom {b} fog {f} fade a {fade_color.a:0.00} {fl} {d} {ts} {gr} {rb}");
            }
        }
        private int trace_postfx_ticks;
        private bool radial_was_on;
        private int radial_was_type;

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
                bloom_dof_weight = cur.bloom_dof_weight,
                diffusion_blur_size = cur.diffusion_blur_size,
                diffusion_bright = cur.diffusion_bright,
                diffusion_threshold = cur.diffusion_threshold,
                diffusion_saturation = cur.diffusion_saturation,
                diffusion_contrast = cur.diffusion_contrast,
            };
            // the game's attribute gates (Director.cs:1307): bit 0x10000 =
            // IsEnableBloom, 0x20000 = IsEnableDiffusion; 1004 authors
            // 0x30000 (both), 1151 0x20000 (diffusion only).
            bloom_diffusion_enabled = (cur.attribute & 0x20000) != 0;
            bloom_state_valid = bloom_state.intensity > 0f || bloom_diffusion_enabled;
        }

        // whether the current bloom key enables the diffusion sub-chain.
        [HideInInspector] public bool bloom_diffusion_enabled;

        // chromatic aberration state (the fork: clamp01(power*0.05)).
        [HideInInspector] public bool chromatic_enabled;
        [HideInInspector] public float chromatic_amount;
        // the current chromatic key's channel offsets + clip, for the blit.
        public (Vector2 red, Vector2 green, Vector2 blue, float clip) chromatic_key_offsets
            = (Vector2.zero, Vector2.zero, Vector2.zero, 1f);

        // lens distortion state (the fork: enabled = |intensity|>0.001).
        [HideInInspector] public bool lens_distortion_enabled;
        [HideInInspector] public float lens_intensity;
        [HideInInspector] public float lens_center_x;
        [HideInInspector] public float lens_center_y;
        [HideInInspector] public float lens_scale;

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
                blink_light_name = cur.blink_light_name,
                blink_light_brightness_power = cur.blink_light_brightness_power,
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
            // the chain selector (uv2_postfx_chain_divergence_audit §2.2):
            // dofBlurType is the game's Execute dispatch, per key. 0 = None,
            // 3 = Bloom (pyramid only, no DOF blur), 1/2/4 fused composites,
            // 5/6 pure DOF chains. UV2's chain implements 1's shape; every
            // blur-bearing type runs it, 0/3 do not.
            dof_chain_type = cur.blur_type;
            bool chain_runs_dof = cur.blur_type != 0 && cur.blur_type != 3;
            dof_enabled = chain_runs_dof && focal_size > 0f && fp > 0f;
            // the coc pass wants eye meters; reconstruct the meters of the
            // resolved focal01 (inverse of WorldToViewportPoint().z/(far-near)).
            dof_focal_m = focal01 * far_near + near;
            dof_focal01 = focal01;
            dof_far_blend = focal_size / far_near * 0.5f + focal01;
            dof_blur_spread = blur;
            dof_foreground_size = fg;
            dof_smoothness = Mathf.Max(0.1f, smooth);

            // ball blur rides the same key (the audit: gate on
            // BallBlurBrightnessIntensity > 0, 1004 up to 8.09 factor).
            float bb_power = cur.ball_blur_power_factor;
            float bb_thresh = cur.ball_blur_brightness_threshold;
            float bb_int = cur.ball_blur_brightness_intensity;
            float bb_spread = cur.ball_blur_spread;
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                bb_power = key_eval.lerp_f(cur.ball_blur_power_factor, next.ball_blur_power_factor, k);
                bb_int = key_eval.lerp_f(cur.ball_blur_brightness_intensity, next.ball_blur_brightness_intensity, k);
            }
            ball_blur_power = bb_power;
            ball_blur_threshold = bb_thresh;
            ball_blur_intensity = bb_int;
            ball_blur_spread = bb_spread;
            // the ball blur passes live on the game's WeightedBlur (type 5)
            // material family only (dof_pipeline_decoded.md: PASS_BALL_BLUR_*
            // on the pure-DOF shader); type 1/3 chains never paint them even
            // when the fields are authored nonzero (1004 authors factor 6.4
            // on type 1/3 keys and the game shows no halo).
            ball_blur_enabled = bb_int > 0f && dof_chain_type == 5;
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

        // radial blur: the game's OnRadialBlur publishes. type/downsample/
        // iteration copy un-lerped; offset/areas/power/ellipse/roll and the
        // depth fields lerp by the next-key rule; type 0 disables the pass.
        // depth front/back normalize by (v-near)/(far-near) clamp01.
        private void eval_radial(float t)
        {
            radial_enabled = false;
            if (ws.postfx.radial_blur.Count == 0) return;
            int i = key_eval.bracket(ws.postfx.radial_blur, t);
            if (i < 0) return;
            var cur = ws.postfx.radial_blur[i];
            var next = i + 1 < ws.postfx.radial_blur.Count ? ws.postfx.radial_blur[i + 1] : null;

            radial_type = cur.move_blur_type;
            if (radial_type <= 0) return;
            radial_downsample = cur.downsample;
            radial_iteration = cur.iteration;

            Vector2 offset = cur.offset;
            float start = cur.start_area;
            float end = cur.end_area;
            float power = cur.power;
            Vector2 ellipse = cur.ellipse_dir;
            float roll = cur.roll_euler_angles;
            float front = cur.depth_power_front;
            float back = cur.depth_power_back;
            Vector4 rect = cur.depth_cancel_rect;
            float blend = cur.depth_cancel_blend_length;
            if (next != null && next.interpolate_type != 0)
            {
                float k = key_eval.interp(cur, next, t);
                offset = Vector2.Lerp(cur.offset, next.offset, k);
                start = key_eval.lerp_f(cur.start_area, next.start_area, k);
                end = key_eval.lerp_f(cur.end_area, next.end_area, k);
                power = key_eval.lerp_f(cur.power, next.power, k);
                ellipse = Vector2.Lerp(cur.ellipse_dir, next.ellipse_dir, k);
                roll = key_eval.lerp_f(cur.roll_euler_angles, next.roll_euler_angles, k);
                front = key_eval.lerp_f(cur.depth_power_front, next.depth_power_front, k);
                back = key_eval.lerp_f(cur.depth_power_back, next.depth_power_back, k);
                rect = Vector4.Lerp(cur.depth_cancel_rect, next.depth_cancel_rect, k);
                blend = key_eval.lerp_f(cur.depth_cancel_blend_length, next.depth_cancel_blend_length, k);
            }

            // _BlurParam (id 212): offset.xy, ellipseDir.xy — the roll applies
            // only to type 5, rotating the ellipse dir (the game composes
            // Quaternion.Euler(0,0,-roll*Deg2Rad) with the camera rotation).
            Vector2 dir = ellipse;
            if (radial_type == 5 && roll != 0f)
            {
                float r = -roll * Mathf.Deg2Rad;
                dir = new Vector2(
                    ellipse.x * Mathf.Cos(r) - ellipse.y * Mathf.Sin(r),
                    ellipse.x * Mathf.Sin(r) + ellipse.y * Mathf.Cos(r));
            }
            radial_blur_param = new Vector4(offset.x, offset.y, dir.x, dir.y);

            // the game's blurAreaDiff clamp then step from power.
            float area_diff = Mathf.Max(blur_area_diff_min, Mathf.Abs(end - start));
            // _BlurParamEx (id 213): step, startArea, areaDiff, depthFront.
            radial_blur_param_ex = new Vector4(power, start, area_diff, 0f);
            radial_end_area = area_diff;

            // depth gates: normalize front/back to the clip range clamp01.
            float near = cam != null ? cam.nearClipPlane : 0.1f;
            float far = cam != null ? cam.farClipPlane : 100f;
            float range = Mathf.Max(1e-4f, far - near);
            radial_depth_on = front != 0f || back != 0f;
            if (radial_depth_on)
            {
                float f01 = Mathf.Clamp01((front - near) / range);
                float b01 = Mathf.Clamp01((back - near) / range);
                radial_blur_param_ex.w = f01;
                radial_end_area = b01;
            }

            // the cancel rect: authored (x,y,w,h) -> min/max, expanded by
            // blendLength when the key asks (expand bit mirrors the game's
            // rect += ±blendLength on all sides).
            radial_rect_on = rect.x != 0f || rect.y != 0f || rect.z != 0f || rect.w != 0f;
            radial_cancel_rect = new Vector4(
                rect.x - blend, rect.y - blend,
                rect.x + rect.z + blend, rect.y + rect.w + blend);
            radial_blend_length = blend;
            radial_enabled = true;
        }

        // chromatic aberration: the fork's consumer is
        // SetChromaticAberration(clamp01(power * 0.05)); gate on is_enable.
        private void eval_chromatic(float t)
        {
            var keys = ws.postfx.chromatic;
            if (keys == null || keys.Count == 0) { chromatic_enabled = false; return; }
            int i = key_eval.bracket(keys, t);
            if (i < 0) { chromatic_enabled = false; return; }
            var cur = keys[i];
            var next = i + 1 < keys.Count ? keys[i + 1] : null;
            float power = cur.power;
            if (next != null && next.interpolate_type != 0)
                power = key_eval.lerp_f(cur.power, next.power, key_eval.interp(cur, next, t));
            chromatic_enabled = cur.is_enable != 0;
            chromatic_amount = Mathf.Clamp01(power * 0.05f);
            chromatic_key_offsets = (cur.red_offset, cur.green_offset, cur.blue_offset,
                cur.clip != 0f ? cur.clip : 1f);
        }

        // lens distortion: enabled when |intensity| > 0.001 (the fork's gate).
        private void eval_lens_distortion(float t)
        {
            var keys = ws.postfx.lens_distortion;
            if (keys == null || keys.Count == 0) { lens_distortion_enabled = false; return; }
            int i = key_eval.bracket(keys, t);
            if (i < 0) { lens_distortion_enabled = false; return; }
            var cur = keys[i];
            var next = i + 1 < keys.Count ? keys[i + 1] : null;
            float intensity = cur.intensity;
            if (next != null && next.interpolate_type != 0)
                intensity = key_eval.lerp_f(cur.intensity, next.intensity, key_eval.interp(cur, next, t));
            lens_distortion_enabled = Mathf.Abs(intensity) > 0.001f;
            lens_intensity = intensity;
            lens_center_x = cur.center_x;
            lens_center_y = cur.center_y;
            lens_scale = cur.scale;
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
