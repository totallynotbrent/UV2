using UnityEngine;

namespace UV2.Live
{
    // publishes the decoded global shader values the game's GraphicSettings sets
    // every frame; without them the chara/stage shaders run on unity defaults
    // (black) on a real gpu.
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

        // the decoded defaults: density 1.0, densityColor 0.5 gray, min 0.
        private const float lightmap_density = 1.0f;
        private const float lightmap_min_density = 0.0f;
        private static readonly Color lightmap_density_color = new(0.5f, 0.5f, 0.5f, 1f);

        // publishes every frame; call from the loader's update.
        public static void publish(Camera cam)
        {
            // lightmap block with the exact decoded folds: both colors scale by
            // 2 first; modulate uses density directly, add clamps 1-density.
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

            // character env: rim/toon white, outline 1.0/1.0 (the .ctor value).
            Shader.SetGlobalColor(id_rim_color, Color.white);
            Shader.SetGlobalVector(id_toon_color, new Vector4(1f, 1f, 1f, 1f));
            Shader.SetGlobalFloat(id_outline_width, 1.0f);
            Shader.SetGlobalFloat(id_outline_offset, 1.0f);

            // lod + depth consumers.
            if (cam != null)
            {
                Shader.SetGlobalFloat(id_camera_fov, Mathf.Min(cam.fieldOfView / 30f, 1f));
                Shader.SetGlobalFloat(id_vertex_depth, cam.farClipPlane - cam.nearClipPlane);
                Shader.SetGlobalFloat(id_far_clip_log, Mathf.Log(cam.farClipPlane));
            }
        }
    }
}
