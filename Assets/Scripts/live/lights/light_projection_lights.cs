using System.Collections.Generic;
using UnityEngine;
using UV2.App;
using UV2.Data;

namespace UV2.Live
{
    // drives the worksheet's lightProjection tracks: one Unity Projector per
    // named entry, cloned from a template on first enable, gobo cookie by
    // textureId from the tex_env_live_cmn_projector bundles, transform +
    // frustum + tint per frame with the fork's lerp toward the next key.
    public static class light_projection_lights
    {
        private class projector_state
        {
            public Projector proj;
            public Material mat;
            public float spin_angle;
            public bool spawned;
        }

        // entry name -> live projector.
        private static readonly Dictionary<string, projector_state> states = new();

        // textureId -> resolved cookie (cache; null = known-missing).
        private static readonly Dictionary<int, Texture> cookies = new();

        private static readonly Dictionary<int, GameObject> templates = new();

        private static bool _census_done;

        // per-frame drive; call from the stage update loop.
        public static void update(float time_sec, List<light_projection_track> tracks)
        {
            if (tracks == null || tracks.Count == 0) return;

            if (!_census_done)
            {
                _census_done = true;
                trace_log.write($"light projection: {tracks.Count} entries authored");
            }

            float frame = time_sec * 60f;
            foreach (var track in tracks)
            {
                if (string.IsNullOrEmpty(track.name)) continue;
                if (track.keys == null || track.keys.Count == 0) continue;

                // current key: last key whose frame <= now.
                int idx = -1;
                for (int i = 0; i < track.keys.Count; i++)
                    if (track.keys[i].frame <= frame) idx = i;
                if (idx < 0) continue;
                var cur = track.keys[idx];
                var next = idx + 1 < track.keys.Count ? track.keys[idx + 1] : null;

                bool enable = cur.is_enable != 0;
                if (!states.TryGetValue(track.name, out var st))
                    states[track.name] = st = new projector_state();

                if (!enable)
                {
                    if (st.proj != null) st.proj.enabled = false;
                    continue;
                }

                // spawn on first enable: a projector GameObject + Light clone.
                if (st.proj == null)
                {
                    var go = new GameObject($"lightproj_{track.name}");
                    var p = go.AddComponent<Projector>();
                    var shader = Shader.Find("Projector/Light");
                    if (shader != null) st.mat = new Material(shader);
                    p.material = st.mat;
                    // the game projects onto everything except the
                    // character layer; UV2 keeps all stage geometry on
                    // Default and characters on Default too, so nothing to
                    // ignore - project onto the whole stage.
                    p.ignoreLayers = 0;
                    st.proj = p;
                    st.spawned = true;
                    trace_log.write($"light projection '{track.name}' spawned");
                }

                st.proj.enabled = true;

                // lerp toward the next key when it interpolates.
                float k = 1f;
                if (next != null && next.interpolate_type != 0)
                    k = key_eval.interp(cur, next, time_sec);

                // transform: position/rotation/scale verbatim (scale fallback one).
                Vector3 pos = cur.position;
                Vector3 ang = cur.angle;
                Vector3 scl = next != null && next.interpolate_type != 0
                    ? Vector3.Lerp(cur.scale, next.scale, k)
                    : cur.scale;
                if (scl == Vector3.zero) scl = Vector3.one;
                st.proj.transform.position = pos;
                st.proj.transform.rotation = Quaternion.Euler(ang);
                st.proj.transform.localScale = scl;

                // frustum.
                st.proj.orthographic = cur.orthographic != 0;
                st.proj.orthographicSize = cur.ortho_size;
                st.proj.nearClipPlane = cur.near_clip;
                st.proj.farClipPlane = cur.far_clip;
                float fov = cur.fov;
                if (fov < 1f || fov > 160f) fov = 30f;
                st.proj.fieldOfView = fov;

                // cookie by textureId.
                if (cookies.TryGetValue(cur.texture_id, out var cookie) && cookie == null)
                {
                    // known-missing; leave whatever the material has
                }
                else if (!cookies.ContainsKey(cur.texture_id) || cookie == null)
                {
                    cookie = resolve_cookie(cur.texture_id);
                    cookies[cur.texture_id] = cookie;
                }
                if (st.mat != null)
                {
                    // the cookie IS the beam: without it the projector light
                    // has no falloff and washes the whole frustum flat. when
                    // the authored gobo bundle is absent, fall back to a
                    // procedural radial falloff so the projection still reads
                    // as a cone-shaped beam, not a full-frustum tint.
                    if (cookie == null && cur.texture_id > 0)
                    {
                        cookie = procedural_gobo();
                        cookies[cur.texture_id] = cookie;
                    }
                    if (cookie != null) st.mat.SetTexture(id_cookie, cookie);
                    float cp = Mathf.Max(0f, cur.color_power);
                    Color tint = cur.color;
                    if (next != null && next.interpolate_type != 0)
                    {
                        tint = Color.Lerp(cur.color, next.color, k);
                        cp = Mathf.Max(0f, key_eval.lerp_f(cur.color_power, next.color_power, k));
                    }
                    st.mat.color = tint * cp;
                }

                // mirror-ball spin: rotate the projector's local axis while
                // the loop-rotation flag is set.
                if (cur.mirror_ball_is_loop_rotation != 0)
                {
                    st.spin_angle += cur.mirror_ball_loop_rotation_speed * Time.deltaTime;
                    st.proj.transform.Rotate(cur.mirror_ball_rotate_axis,
                        cur.mirror_ball_loop_rotation_speed * Time.deltaTime, Space.Self);
                }
            }
        }

        // builds a radial-falloff disc cookie for entries whose authored
        // gobo bundle is absent: bright core fading to black at the rim, the
        // generic projector-cookie shape.
        private static Texture procedural_gobo()
        {
            if (gobo_fallback != null) return gobo_fallback;
            const int n = 128;
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            gobo_fallback = tex;
            return tex;
        }

        private static Texture gobo_fallback;

        // resolves the gobo cookie from the projector texture bundles.
        private static Texture resolve_cookie(int texture_id)
        {
            if (texture_id <= 0) return null;
            string key = $"3d/env/live/common/tex_env_live_cmn_projector{texture_id:000}";
            using var meta = meta_reader.reader.open(config.meta_db_path);
            var rows = meta?.lookup(new HashSet<string> { key });
            var row = rows?.GetValueOrDefault(key);
            if (row == null)
            {
                trace_log.write($"light projection: no meta row for cookie {key}");
                return null;
            }
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null)
            {
                trace_log.write($"light projection: cookie bundle failed {key}");
                return null;
            }
            var asset_name = key.Substring(key.LastIndexOf('/') + 1);
            if (!bundle.Contains(asset_name))
            {
                trace_log.write($"light projection: cookie asset '{asset_name}' not in bundle");
                return null;
            }
            return bundle.LoadAsset<Texture>(asset_name);
        }

        public static void reset()
        {
            foreach (var st in states.Values)
                if (st.proj != null) Object.Destroy(st.proj.gameObject);
            states.Clear();
            cookies.Clear();
            _census_done = false;
        }

        private static readonly int id_cookie = Shader.PropertyToID("_ShadowTex");
    }
}
