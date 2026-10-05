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

        // the pre-driver ambient floor: unity's trilight sky color, replaced
        // by the bg_color1 director's publish once the worksheet runs.
        private static Color ambient_fallback = new(0.212f, 0.227f, 0.259f, 1f);
        public static void set_ambient_fallback(Color c) => ambient_fallback = c;

        private const float lightmap_density = 1.0f;
        private const float lightmap_min_density = 0.0f;
        private static readonly Color lightmap_density_color = new(0.5f, 0.5f, 0.5f, 1f);

        // stores the global-light keys bracketing the current frame for the blend.
        public static void set_light_track(global_light_key key, float blend)
        {
            _light_key = key;
            _light_next = null;
            _light_blend = blend;
        }
        public static void set_light_track(global_light_key key, global_light_key next, float blend)
        {
            _light_key = key;
            _light_next = next;
            _light_blend = blend;
        }
        private static global_light_key _light_key;
        private static global_light_key _light_next;
        private static float _light_blend;

        private static float lerp_f(float a, float b, float blend) => Mathf.Lerp(a, b, blend);
        private static Color lerp_c(Color a, Color b, float blend) => Color.Lerp(a, b, blend);

        // the assembled character mpb: rebuilt when the light track moves.
        private static MaterialPropertyBlock _chara_mpb;
        private static Vector3 _last_light_dir = new(12345f, 0f, 0f);

        // applies the toon-light + rim block onto every character renderer.
        public static void publish_chara_block(List<Transform> chara_roots)
        {
            if (chara_roots == null || chara_roots.Count == 0) return;
            if (_chara_mpb == null) _chara_mpb = new MaterialPropertyBlock();

            Vector3 dir = Vector3.down;
            var k = _light_key;
            var n = _light_next;
            if (k != null && k.light_dir.sqrMagnitude > 1e-06f)
            {
                Vector3 euler = k.light_dir;
                if (n != null && n.light_dir.sqrMagnitude > 1e-06f)
                    euler = Vector3.Lerp(k.light_dir, n.light_dir, _light_blend);
                dir = -(Quaternion.Euler(euler) * Vector3.forward).normalized;
            }

            if ((dir - _last_light_dir).sqrMagnitude < 1e-10f) return;
            _last_light_dir = dir;

            _chara_mpb.SetFloat(id_use_orig_light, 1f);
            _chara_mpb.SetVector(id_orig_light_dir, dir);

            // the game publishes the toon light direction via Material::SetVector
            // (ModelController::UpdateBodyLightDir 0x7ff8e51a8010), not only a
            // property block — a later SetPropertyBlock (the bg_color1 tint)
            // would otherwise wipe it. pin use_orig + dir on shared materials.
            foreach (var root in chara_roots)
            {
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    try
                    {
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            if (m.HasProperty(id_use_orig_light)) m.SetFloat(id_use_orig_light, 1f);
                            if (m.HasProperty(id_orig_light_dir)) m.SetVector(id_orig_light_dir, dir);
                        }
                    }
                    catch { /* destroyed mid-shutdown */ }
                }
            }

            if (k != null)
            {
                var nx = _light_next ?? k;
                float b = _light_next != null ? _light_blend : 0f;
                _chara_mpb.SetColor(Shader.PropertyToID("_RimColor"), lerp_c(k.rim_color, nx.rim_color, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimStep"), lerp_f(k.rim_step, nx.rim_step, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimFeather"), lerp_f(k.rim_feather, nx.rim_feather, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimSpecRate"), lerp_f(k.rim_spec_rate, nx.rim_spec_rate, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimShadowRate"), lerp_f(k.rim_shadow_rate, nx.rim_shadow_rate, b));
                _chara_mpb.SetColor(Shader.PropertyToID("_RimColor2"), lerp_c(k.rim_color2, nx.rim_color2, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimStep2"), lerp_f(k.rim_step2, nx.rim_step2, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimFeather2"), lerp_f(k.rim_feather2, nx.rim_feather2, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimSpecRate2"), lerp_f(k.rim_spec_rate2, nx.rim_spec_rate2, b));
                _chara_mpb.SetFloat(Shader.PropertyToID("_RimShadowRate2"), lerp_f(k.rim_shadow_rate2, nx.rim_shadow_rate2, b));
            }

            foreach (var root in chara_roots)
            {
                // shutdown can destroy a root or renderer mid-update; the rest still publish.
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    try { r.SetPropertyBlock(_chara_mpb); }
                    catch { /* destroyed mid-shutdown */ }
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

            Shader.SetGlobalColor(id_rim_color, Color.white);
            Shader.SetGlobalVector(id_toon_color, new Vector4(1f, 1f, 1f, 1f));
            Shader.SetGlobalFloat(id_outline_width, 1.0f);
            Shader.SetGlobalFloat(id_outline_offset, 1.0f);

            // the dirt + ambient + array globals.
            Shader.SetGlobalColor(Shader.PropertyToID("_GlobalDirtRimSpecularColor"), new Color(0.25f, 0.25f, 0.25f, 1f));
            Shader.SetGlobalColor(Shader.PropertyToID("_GlobalDirtToonColor"), new Color(0.5f, 0.5f, 0.5f, 1f));
            Shader.SetGlobalColor(Shader.PropertyToID("_GlobalDirtColor"), new Color(0.6f, 0.451f, 0.384f, 1f));
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
        }

        // publishes the fog-off state so stage shaders never fade the frame.
        private static void publish_fog_off()
        {
            Shader.SetGlobalColor(id_fog_color, Color.clear);
            Shader.SetGlobalVector(id_fog_min_distance, new Vector4(100000f, 0f, 0f, 0f));
            Shader.SetGlobalVector(id_fog_length, new Vector4(0f, 0f, 0f, 1e-06f));
            Shader.SetGlobalFloat(id_fog_max_density, 1f);
            Shader.SetGlobalFloat(id_fog_max_height, 100f);
            Shader.SetGlobalVector(id_fog_world_origin, Vector4.zero);
        }

        // the toon light direction from the global-light track; straight down when no track.
        private static void publish_toon_light()
        {
            Shader.SetGlobalFloat(id_use_orig_light, 1f);
            var dir = Vector3.down;
            if (_light_key != null)
            {
                var k = _light_key;
                Vector3 light_dir = k.light_dir;
                if (light_dir.sqrMagnitude < 1e-06f)
                    light_dir = Vector3.down;
                // the euler angles point the light; the shader wants the travel direction.
                var rot = Quaternion.Euler(light_dir);
                dir = -(rot * Vector3.forward).normalized;
            }
            Shader.SetGlobalVector(id_orig_light_dir, dir);
        }
    }
}
