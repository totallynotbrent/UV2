using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // publishes the global shader values the chara/stage shaders need every frame.
    public static class global_shade
    {
        private static readonly int id_lightmap_color = Shader.PropertyToID("_Global_LightmapColor");
        private static readonly int id_lightmap_shadow = Shader.PropertyToID("_Global_LightmapShadowColor");
        private static readonly int id_lightmap_add = Shader.PropertyToID("_Global_LightmapDensityAddColor");
        private static readonly int id_lightmap_modulate = Shader.PropertyToID("_Global_LightmapModulateColor");

        private static readonly int id_rim_color = Shader.PropertyToID("_GlobalRimColor");
        private static readonly int id_toon_color = Shader.PropertyToID("_GlobalToonColor");
        private static readonly int id_outline_width = Shader.PropertyToID("_GlobalOutlineWidth");
        private static readonly int id_outline_offset = Shader.PropertyToID("_GlobalOutlineOffset");

        private static readonly int id_camera_fov = Shader.PropertyToID("_GlobalCameraFov");
        private static readonly int id_vertex_depth = Shader.PropertyToID("_GlobalVertexDepthLinear");
        private static readonly int id_far_clip_log = Shader.PropertyToID("_GlobalFarClipLog");

        // the fog globals, published in their off state.
        private static readonly int id_fog_color = Shader.PropertyToID("_Global_FogColor");
        private static readonly int id_fog_min_distance = Shader.PropertyToID("_Global_FogMinDistance");
        private static readonly int id_fog_length = Shader.PropertyToID("_Global_FogLength");
        private static readonly int id_fog_max_density = Shader.PropertyToID("_Global_MaxDensity");
        private static readonly int id_fog_max_height = Shader.PropertyToID("_Global_MaxHeight");
        private static readonly int id_fog_world_origin = Shader.PropertyToID("_Global_FogWorld_Origin");

        // the toon light direction comes from the worksheet, not a unity light object.
        private static readonly int id_use_orig_light = Shader.PropertyToID("_UseOriginalDirectionalLight");
        private static readonly int id_orig_light_dir = Shader.PropertyToID("_OriginalDirectionalLightDir");
        // the main-light globals: the game's GlobalShaderLightParameter::Update
        // (0x7ff8e4d7c1e0) publishes normalize(-transform.forward) as
        // _MainLightPosition (w=0) and the light color as _MainLightColor via
        // Shader::SetGlobalVector. in the concert scene (zero Light objects)
        // these are the chara PS's fallback light (cb0[6]/cb0[7]) when
        // _UseOriginalDirectionalLight is 0.
        private static readonly int id_main_light_pos = Shader.PropertyToID("_MainLightPosition");
        private static readonly int id_main_light_color = Shader.PropertyToID("_MainLightColor");

        // the pre-driver ambient floor: unity's trilight sky color, replaced
        // by the bg_color1 director's publish once the worksheet runs.
        private static Color ambient_fallback = new(0.212f, 0.227f, 0.259f, 1f);
        public static void set_ambient_fallback(Color c) => ambient_fallback = c;

        private const float lightmap_density = 1.0f;
        private const float lightmap_min_density = 0.0f;
        private static readonly Color lightmap_density_color = new(0.5f, 0.5f, 0.5f, 1f);

        // the per-roster-slot light states the director resolved this frame.
        // the game's global light is a per-cast-member publish: slot i is
        // reached only when the current key's mask admits bit i, so a
        // character the mask excludes keeps whatever state it already had.
        private static IReadOnlyList<chara_light_state> _slot_states;
        private static Color _scene_rim = Color.white;
        private static Quaternion _scene_rotation = Quaternion.identity;

        // the resolved scene-wide light state (the last list that published)
        // plus the per-slot states the character blocks draw from.
        public static void set_slot_states(IReadOnlyList<chara_light_state> states, Color scene_rim,
            Quaternion scene_rotation)
        {
            _slot_states = states;
            _scene_rim = scene_rim;
            _scene_rotation = scene_rotation;
        }

        // the assembled character mpb: rebuilt when the light track moves.
        private static MaterialPropertyBlock _chara_mpb;
        private static bool _pin_log_pending;
        private static bool _pin_log_done;

        // applies the toon-light + rim block onto every character renderer.
        // places holds each root's standing place (the game's roster index):
        // the mask bit i selects roster slot i, and the cast list is denser
        // than the number of loaded roots.
        public static void publish_chara_block(List<Transform> chara_roots, IReadOnlyList<int> places = null)
        {
            if (chara_roots == null || chara_roots.Count == 0) return;
            if (_chara_mpb == null) _chara_mpb = new MaterialPropertyBlock();

            // per-character gate: only the roster slots whose bit the current
            // key's mask admits get the block (fork 451-464). a slot the mask
            // excludes is skipped, not reset.
            for (int ci = 0; ci < chara_roots.Count; ci++)
            {
                var root = chara_roots[ci];
                // shutdown can destroy a root or renderer mid-update; the rest still publish.
                if (root == null) continue;
                int place = places != null && ci < places.Count ? places[ci] : ci;
                if (_slot_states == null || place < 0 || place >= _slot_states.Count) continue;
                var state = _slot_states[place];
                if (state == null || !state.valid) continue;

                Vector3 dir = -(state.rotation * Vector3.forward).normalized;

                // the game re-applies this block EVERY frame (fork Director.cs
                // OnUpdateGlobalLight: the block is republished per frame so
                // any competing writer loses). the direction-delta early-out
                // let bg_color1's per-frame Chara* tint wipe the toon-light
                // state after the first frame it settled, so the body fell
                // back to the serialized defaults (hot) for the rest of the
                // song - the white-body wash. republish unconditionally.
                _chara_mpb.SetFloat(id_use_orig_light, 1f);
                _chara_mpb.SetVector(id_orig_light_dir, dir);
                // arm the one-shot pin log only once per apply-pass owner; arming
                // it every frame re-logged the whole cast at ~30 lines/sec and
                // drowned the trace (the republish itself stays unconditional).
                if (!_pin_log_done) _pin_log_pending = true;

                // the game publishes the toon light direction via Material::SetInt/SetVector
                // (ModelController::UpdateBodyLightDir 0x7ff8e51a8010) with NO HasProperty
                // check - a stripped variant simply ignores the write. the
                // property block carries the same pair per renderer, so the
                // state stays per character without touching the shared
                // material (a material write would leak one slot's light onto
                // every character wearing that material).
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    // setpropertyblock replaces wholesale. bg_color1's Chara*
                    // groups write the tint block on these same renderers
                    // every frame, so read the current block back and extend
                    // it with the light state instead of clobbering (the
                    // fork's facial handler documents this exact pattern).
                    // the rim values are written after the read-back so they
                    // survive the merge.
                    try
                    {
                        r.GetPropertyBlock(_chara_mpb);
                        _chara_mpb.SetColor(Shader.PropertyToID("_RimColor"), state.rim_color);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimStep"), state.rim_step);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimFeather"), state.rim_feather);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimSpecRate"), state.rim_spec_rate);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimShadowRate"), state.rim_shadow_rate);
                        _chara_mpb.SetColor(Shader.PropertyToID("_RimColor2"), state.rim_color2);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimStep2"), state.rim_step2);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimFeather2"), state.rim_feather2);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimSpecRate2"), state.rim_spec_rate2);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimShadowRate2"), state.rim_shadow_rate2);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimHorizonOffset"), state.rim_horizon_offset);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimVerticalOffset"), state.rim_vertical_offset);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimHorizonOffset2"), state.rim_horizon_offset2);
                        _chara_mpb.SetFloat(Shader.PropertyToID("_RimVerticalOffset2"), state.rim_vertical_offset2);
                        _chara_mpb.SetFloat(id_use_orig_light, 1f);
                        _chara_mpb.SetVector(id_orig_light_dir, dir);
                        r.SetPropertyBlock(_chara_mpb);
                    }
                    catch { /* destroyed mid-shutdown */ }
                }

                // one-shot diagnostic: whether the loaded variant carries
                // the toon-light props (llvmpipe benches read False -
                // variant stripping; his DX11 trace reads True). dumps
                // the name table once so the bundle-skew hypothesis is
                // checkable from the trace alone.
                if (_pin_log_pending)
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>())
                    {
                        if (r == null) continue;
                        var m0 = r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials[0] : null;
                        if (m0 == null) continue;
                        var sb = new System.Text.StringBuilder();
                        var sh = m0.shader;
                        int prop_count = sh.GetPropertyCount();
                        int printed = 0;
                        for (int pi = 0; pi < prop_count && printed < 12; pi++)
                        {
                            var pname = sh.GetPropertyName(pi);
                            if (pname == null) continue;
                            if (pname.Contains("Light") || pname.Contains("Toon") || pname.Contains("Rim"))
                            {
                                if (printed++ > 0) sb.Append(' ');
                                sb.Append(pname);
                            }
                        }
                        trace_log.write($"shade pin: '{r.name}' mat '{m0.name}' acceptsUseOrig={m0.HasProperty(id_use_orig_light)} shader '{sh.name}' props({prop_count}) [{sb}]");
                        _pin_log_pending = false;
                        _pin_log_done = true;
                        break;
                    }
                }
            }
        }

        // the scene rig: one directional sun + one audio listener, created once.
        private static bool _rig_ready;
        private static GameObject _sun;
        private static GameObject _listener_host;

        private static void ensure_scene_rig(Camera cam)
        {
            if (_rig_ready) return;

            if (GameObject.Find("AudioListener") == null)
            {
                _listener_host = new GameObject("AudioListener");
                _listener_host.AddComponent<AudioListener>();
            }

            var existing_lights = Object.FindObjectsOfType<Light>();
            bool has_dir = false;
            foreach (var l in existing_lights) if (l.type == LightType.Directional) has_dir = true;
            if (!has_dir)
            {
                // the game's concert scene carries zero Light objects: shading
                // is globals + property blocks. keep the sun dark (intensity 0)
                // so any consumer expecting a directional reference still has
                // one without lifting the whole stage.
                _sun = new GameObject("Directional Light");
                var light = _sun.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                _sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            RenderSettings.sun = null; // the chara mpb drives toon light, not the sun

            _rig_ready = true;
            trace_log.write("scene rig: directional sun + audio listener live");
        }

        // publishes every frame; call from the loader's update.
        public static void publish(Camera cam)
        {
            ensure_scene_rig(cam);
            publish_fog_off();
            // lightmap block: both colors scale by 2; modulate uses density, add clamps 1-density.
            float density_add = Mathf.Max(1f - lightmap_density, lightmap_min_density);
            Vector3 add_rgb = new(lightmap_density_color.r * 2f * density_add,
                                  lightmap_density_color.g * 2f * density_add,
                                  lightmap_density_color.b * 2f * density_add);
            Vector3 mod_rgb = new(lightmap_density_color.r * 2f * lightmap_density,
                                  lightmap_density_color.g * 2f * lightmap_density,
                                  lightmap_density_color.b * 2f * lightmap_density);
            Shader.SetGlobalColor(id_lightmap_color, Color.white);
            Shader.SetGlobalColor(id_lightmap_shadow, new Color(0.25f, 0.25f, 0.25f, 1f));
            Shader.SetGlobalColor(id_lightmap_add, new Color(add_rgb.x, add_rgb.y, add_rgb.z, 1f));
            Shader.SetGlobalColor(id_lightmap_modulate, new Color(mod_rgb.x, mod_rgb.y, mod_rgb.z, 1f));

            // the global rim tint is the authored one: the worksheet's
            // global-light track carries rimColor per key (song 1004 f2400 =
            // 0.354/0.587/1.0), so a constant white rim is a washed-out
            // cast. chara renderers override it with their own _RimColor.
            Shader.SetGlobalColor(id_rim_color, _scene_rim);
            Shader.SetGlobalVector(id_toon_color, new Vector4(1f, 1f, 1f, 1f));
            Shader.SetGlobalFloat(id_outline_width, 1.0f);
            Shader.SetGlobalFloat(id_outline_offset, 1.0f);

            // the dirt + ambient + array globals. the dirt triplet matches the
            // game's static defaults (0x1b82cde4ee0 +0x24/34/44): dirt color,
            // dirt rim/spec, dirt toon.
            Shader.SetGlobalColor(Shader.PropertyToID("_GlobalDirtColor"), new Color(0.35f, 0.254f, 0.215f, 1f));
            Shader.SetGlobalColor(Shader.PropertyToID("_GlobalDirtRimSpecularColor"), new Color(0.6f, 0.451f, 0.384f, 1f));
            Shader.SetGlobalColor(Shader.PropertyToID("_GlobalDirtToonColor"), new Color(0.28f, 0.19f, 0.14f, 1f));
            // the bgColor1 driver owns _AmbientColor now (color*colorPower
            // from the worksheet); the constant here was the showroom floor.
            Shader.SetGlobalColor(Shader.PropertyToID("_AmbientColor"), ambient_fallback);
            Shader.SetGlobalFloat(Shader.PropertyToID("_CylinderBlend"), 0f);
            Shader.SetGlobalFloat(Shader.PropertyToID("_RimHorizonOffset"), 0f);
            Shader.SetGlobalFloat(Shader.PropertyToID("_UVEmissivePower"), 0f);
            Shader.SetGlobalColor(Shader.PropertyToID("_RimColor2"), Color.black);
            Shader.SetGlobalVectorArray(Shader.PropertyToID("_MainParam"), new Vector4[] { Vector4.zero, Vector4.zero });
            Shader.SetGlobalVectorArray(Shader.PropertyToID("_HighParam1"), new Vector4[] { new Vector4(0, 0, 0, 1), new Vector4(0, 0, 0, 1), Vector4.zero });
            Shader.SetGlobalVectorArray(Shader.PropertyToID("_HighParam2"), new Vector4[] { new Vector4(0, 0, 0, 1), new Vector4(0, 0, 0, 1) });
            var color_array = new Vector4[10];
            for (int i = 0; i < 10; i++) color_array[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(Shader.PropertyToID("_ColorArray"), color_array);
            Shader.SetGlobalFloatArray(Shader.PropertyToID("_DirtRate"), new float[] { 0f, 0f, 0f });

            if (cam != null)
            {
                Shader.SetGlobalFloat(id_camera_fov, Mathf.Min(cam.fieldOfView / 30f, 1f));
                Shader.SetGlobalFloat(id_vertex_depth, cam.farClipPlane - cam.nearClipPlane);
                Shader.SetGlobalFloat(id_far_clip_log, Mathf.Log(cam.farClipPlane));
            }

            publish_toon_light();

            // the main-light globals, mirroring GlobalShaderLightParameter::Update:
            // the direction the shader falls back to when _UseOriginalDirectionalLight
            // is 0, plus the color the chara PS multiplies albedo/toon/spec by
            // (cb0[7]). white light, direction from the same worksheet track
            // (the game publishes from its light rig; ours is the global-light
            // track's current direction).
            Vector3 main_dir = -(_scene_rotation * Vector3.forward).normalized;
            Shader.SetGlobalVector(id_main_light_pos, new Vector4(main_dir.x, main_dir.y, main_dir.z, 0f));
            Shader.SetGlobalColor(id_main_light_color, Color.white);

            // feed the render-loop republish pass: urp's SetupLights clobbers
            // both globals after this publish, and the pass restores them.
            UV2.Live.fog_fade_feature.set_main_light_dir(main_dir);
        }

        // publishes the game's live fog-off state (GraphicSettings::UpdateFog
        // pushes these every frame of a live; live heap read in
        // uv2_chara_fog_factor_decoded.md §4). fog color is the PALE CYAN
        // default, not black, and MaxDensity=1 floors the chara VS clarity
        // factor at 1 — both publishes give full chara color, but any
        // partial-fog path fades through the right color.
        private static void publish_fog_off()
        {
            Shader.SetGlobalColor(id_fog_color, new Color(0.76f, 1.00f, 1.00f, 1f));
            Shader.SetGlobalVector(id_fog_min_distance, new Vector4(0f, 0f, 0f, 0f));
            Shader.SetGlobalVector(id_fog_length, new Vector4(1000f, 1000f, 1000f, 1000f));
            Shader.SetGlobalFloat(id_fog_max_density, 1f);
            Shader.SetGlobalFloat(id_fog_max_height, 100f);
            Shader.SetGlobalVector(id_fog_world_origin, Vector4.zero);
        }

        // the toon light direction from the global-light track; straight down when no track.
        private static void publish_toon_light()
        {
            Shader.SetGlobalFloat(id_use_orig_light, 1f);
            // the euler angles point the light; the shader wants the travel direction.
            Vector3 dir = -(_scene_rotation * Vector3.forward).normalized;
            Shader.SetGlobalVector(id_orig_light_dir, dir);
        }
    }
}
