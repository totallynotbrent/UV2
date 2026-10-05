using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UV2.Live
{
    // the screen-space fog + fade pass: the classic GlobalFog frustum-ray math
    // (the game reuses it verbatim) plus the authored fade quad on top.
    public class fog_fade_feature : ScriptableRendererFeature
    {
        private class pass : ScriptableRenderPass
        {
            private fog_fade_feature owner;
            private Material mat;
            private postfx_director director;

            public pass(fog_fade_feature owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            }

            private void ensure_mat()
            {
                if (mat == null && owner.shader != null)
                    mat = CoreUtils.CreateEngineMaterial(owner.shader);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                ensure_mat();
                if (director == null)
                    director = UnityEngine.Object.FindObjectOfType<postfx_director>();
                if (mat == null || director == null) return;
                var cam = renderingData.cameraData.camera;

                bool fade_on = director.fade_color.a > 0.001f;
                bool fog_on = director.fog_enabled && director.fog_state != null
                              && director.fog_state.fog_mode >= 1;
                bool film_on = film_valid(director.film1_state)
                            || film_valid(director.film2_state)
                            || film_valid(director.film3_state);
                bool dof_on = director.dof_enabled;
                bool bloom_on = director.bloom_state_valid && director.bloom_state != null;
                if (!fade_on && !fog_on && !film_on && !dof_on && !bloom_on) return;

                // blitting a target onto itself is undefined in urp: d3d unbinds
                // the srv when the texture becomes the render target, so the
                // overlay samples black. every stage ping-pongs through two
                // temporary targets and the result copies back at the end.
                var color = renderingData.cameraData.renderer.cameraColorTarget;
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                CommandBuffer cmd = CommandBufferPool.Get("uv2 postfx chain");
                cmd.GetTemporaryRT(tmp_a_id, desc, FilterMode.Bilinear);
                cmd.GetTemporaryRT(tmp_b_id, desc, FilterMode.Bilinear);
                var tmp_a = new RenderTargetIdentifier(tmp_a_id);
                var tmp_b = new RenderTargetIdentifier(tmp_b_id);

                RenderTargetIdentifier cur = color;
                bool cur_is_color = true;
                RenderTargetIdentifier nxt = tmp_a;
                bool nxt_is_a = true;

                // the bloom pyramid runs first — the game's chain order is
                // bloom, then dof, then the film overlay layers.
                if (bloom_on)
                    apply_bloom(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                if (fog_on || fade_on)
                {
                    if (fog_on)
                    {
                        var f = director.fog_state;
                        // the classic globals the consumer reads (fog publisher
                        // decode ids 242-248): the four corner rays as matrix
                        // rows, camera position, height/dist params, mode.
                        Transform tr = cam.transform;
                        float camY = tr.position.y;
                        var cam_t = cam.transform;
                        Vector3[] corners = new Vector3[4];
                        cam.CalculateFrustumCorners(new Rect(0, 0, 1, 1),
                            cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, corners);
                        // to world space (classic SetFogRows: corner * far already
                        // applied by CalculateFrustumCorners; rotate into world).
                        for (int ci = 0; ci < 4; ci++)
                            corners[ci] = cam_t.TransformDirection(corners[ci]);
                        var frustum_rows = Matrix4x4.identity;
                        frustum_rows.SetRow(0, corners[0]); // bottom-left
                        frustum_rows.SetRow(1, corners[3]); // top-left
                        frustum_rows.SetRow(2, corners[1]); // bottom-right
                        frustum_rows.SetRow(3, corners[2]); // top-right
                        mat.SetMatrix("_FrustumCornersWS", frustum_rows);
                        mat.SetVector("_CameraWS", new Vector4(tr.position.x, tr.position.y, tr.position.z, 1f));
                        mat.SetVector("_HeightParams", new Vector4(
                            f.start_distance, camY - f.start_distance,
                            f.is_height != 0 ? 1f : 0f, f.end * 0.5f));
                        mat.SetVector("_DistanceParams", new Vector4(
                            f.exp_density > 0f ? -f.exp_density : 0f, 0, 0, 0));
                        mat.SetColor("_FogColor", f.color);
                        if (f.fog_mode == 1)
                            mat.SetVector("_SceneFogParams", new Vector4(
                                0f, 0f, 1f, 1f / Mathf.Max(0.0001f, f.end - f.start)));
                        else
                            mat.SetVector("_SceneFogParams", new Vector4(
                                f.fog_mode == 3 ? f.exp_density * 1.4427f : f.exp_density * 1.20112f,
                                1f, 0f, 0f));
                        mat.SetVector("_SceneFogMode", new Vector4(
                            (float)(f.fog_mode - 1),
                            f.use_radial_distance != 0 ? 1f : 0f, 0, 0));
                        mat.SetFloat("_HeightDensity", f.height_density);
                        cmd.EnableShaderKeyword("FOG_HEIGHT_ON");
                    }
                    else
                    {
                        cmd.DisableShaderKeyword("FOG_HEIGHT_ON");
                    }
                    if (fade_on)
                    {
                        mat.SetColor("_FadeColor", director.fade_color);
                        cmd.EnableShaderKeyword("FADE_ON");
                    }
                    else
                    {
                        cmd.DisableShaderKeyword("FADE_ON");
                    }
                    cmd.Blit(cur, nxt, mat);
                    cur = nxt; cur_is_color = false;
                    nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
                }

                // the dof chain: coc prefilter -> blur ping-pong -> composite.
                if (dof_on)
                    apply_dof(cmd, ref renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // the film overlays: each valid layer blits on top in order,
                // exactly the game's PostFilmBlit layer chain.
                if (film_valid(director.film1_state))
                    apply_film(cmd, renderingData, director.film1_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                if (film_valid(director.film2_state))
                    apply_film(cmd, renderingData, director.film2_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                if (film_valid(director.film3_state))
                    apply_film(cmd, renderingData, director.film3_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                if (!cur_is_color)
                    cmd.Blit(cur, color);

                cmd.ReleaseTemporaryRT(tmp_a_id);
                cmd.ReleaseTemporaryRT(tmp_b_id);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            private static readonly int tmp_a_id = Shader.PropertyToID("_uv2_postfx_tmp_a");
            private static readonly int tmp_b_id = Shader.PropertyToID("_uv2_postfx_tmp_b");

            private Material dof_mat;
            private RenderTexture dof_rt_a;
            private RenderTexture dof_rt_b;

            // the game's fastbloom pyramid: three same-res blits at quarter
            // resolution with the threshold folded per level and intensity
            // entering exactly once at the downsample, then a soft-add
            // composite back onto the chain (no composite-time scale).
            private Material bloom_mat;
            private RenderTexture bloom_rt_a;
            private RenderTexture bloom_rt_b;

            private void apply_bloom(CommandBuffer cmd, RenderingData renderingData,
                                     postfx_director director,
                                     ref RenderTargetIdentifier cur, ref bool cur_is_color,
                                     ref RenderTargetIdentifier nxt, ref bool nxt_is_a,
                                     RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (bloom_mat == null)
                {
                    var sh = Shader.Find("live/uv2_bloom");
                    if (sh == null) return;
                    bloom_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                int bw = Mathf.Max(1, desc.width / 4), bh = Mathf.Max(1, desc.height / 4);
                if (bloom_rt_a == null || bloom_rt_a.width != bw || bloom_rt_a.height != bh)
                {
                    if (bloom_rt_a != null) bloom_rt_a.Release();
                    if (bloom_rt_b != null) bloom_rt_b.Release();
                    bloom_rt_a = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_b = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                }

                var b = director.bloom_state;
                float aspect = (float)desc.width / Mathf.Max(1, desc.height);
                // the decode's per-level publishes: A = (1/w, 1/h, threshold,
                // intensity); C/D = (blur*2^-9/aspect, blur*2^-9, threshold,
                // intensity). the threshold fold repeats every level.
                bloom_mat.SetVector("_Parameter", new Vector4(
                    1f / bw, 1f / bh, b.threshold, b.intensity));
                cmd.Blit(cur, bloom_rt_a, bloom_mat, 0);
                bloom_mat.SetVector("_Parameter", new Vector4(
                    b.blur_size * Mathf.Pow(2f, -9f) / aspect,
                    b.blur_size * Mathf.Pow(2f, -9f),
                    b.threshold, b.intensity));
                cmd.Blit(bloom_rt_a, bloom_rt_b, bloom_mat, 1);
                cmd.Blit(bloom_rt_b, bloom_rt_a, bloom_mat, 2);
                // composite: source + blurred energy, soft-add; never scaled.
                bloom_mat.SetTexture("_BloomTex", bloom_rt_a);
                bloom_mat.SetFloat("_BloomGate", 0f);
                cmd.Blit(cur, nxt, bloom_mat, 3);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }

            private void apply_dof(CommandBuffer cmd, ref RenderingData renderingData,
                                   postfx_director director, ref RenderTargetIdentifier cur,
                                   ref bool cur_is_color, ref RenderTargetIdentifier nxt,
                                   ref bool nxt_is_a, RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (dof_mat == null)
                {
                    var sh = Shader.Find("live/uv2_dof");
                    if (sh == null) return;
                    dof_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                var cam = renderingData.cameraData.camera;
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                if (dof_rt_a == null || dof_rt_a.width != desc.width || dof_rt_a.height != desc.height)
                {
                    if (dof_rt_a != null) dof_rt_a.Release();
                    if (dof_rt_b != null) dof_rt_b.Release();
                    dof_rt_a = new RenderTexture(desc.width / 2, desc.height / 2, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    dof_rt_b = new RenderTexture(desc.width / 2, desc.height / 2, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                }

                dof_mat.SetVector("_DofCurveParams", new Vector4(1f, 1f, director.dof_far_blend, 0f));
                dof_mat.SetVector("_DofFocusParams", new Vector4(
                    director.dof_focal01, director.dof_blur_spread,
                    director.dof_foreground_size, director.dof_smoothness));
                dof_mat.SetFloat("_DofBlurSpread", director.dof_blur_spread);
                dof_mat.SetVector("_DofFocalMeters", new Vector4(
                    director.dof_focal_m, cam.farClipPlane, 0f, 0f));

                // the chain runs on its own half-res pair; the composite writes
                // back into the chain's current target.
                cmd.Blit(cur, dof_rt_a, dof_mat, 0);   // coc prefilter
                cmd.Blit(dof_rt_a, dof_rt_b, dof_mat, 1); // horizontal
                cmd.Blit(dof_rt_b, dof_rt_a, dof_mat, 2); // vertical
                dof_mat.SetTexture("_DofBlurred", dof_rt_a);
                cmd.Blit(cur, nxt, dof_mat, 3);        // composite
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }

            private static bool film_valid(postfx_director.film_state s)
                => s != null && s.valid && s.mode > 0;

            private Material film_mat;

            private static readonly string[] film_keywords =
            {
                "MODE_NONE", "MODE_LERP", "MODE_ADD", "MODE_MUL",
                "MODE_VIGNETTE_LERP", "MODE_VIGNETTE_ADD", "MODE_VIGNETTE_MUL",
                "MODE_MONOCHROME", "MODE_SCREENBLEND", "MODE_VIGNETT_SCREENBLEND",
            };

            private void apply_film(CommandBuffer cmd, RenderingData renderingData,
                                    postfx_director.film_state s,
                                    ref RenderTargetIdentifier cur, ref bool cur_is_color,
                                    ref RenderTargetIdentifier nxt, ref bool nxt_is_a,
                                    RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (film_mat == null)
                {
                    var sh = Shader.Find("live/uv2_film");
                    if (sh == null) return;
                    film_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                // the game's keyword sub: disable every keyword then enable list[mode].
                for (int i = 0; i < film_keywords.Length; i++)
                    film_mat.DisableKeyword(film_keywords[i]);
                if (s.mode >= 0 && s.mode < film_keywords.Length)
                    film_mat.EnableKeyword(film_keywords[s.mode]);
                film_mat.SetFloat("_PostFilmPower", s.power);
                film_mat.SetFloat("_DepthPower", s.depth_power);
                // the game publishes max(0, 1.5 - clip) into _DepthClip
                // (composite decode, id 263).
                film_mat.SetFloat("_DepthClip", Mathf.Max(0f, 1.5f - s.depth_clip));
                film_mat.SetVector("_PostFilmOffsetParam", new Vector4(s.offset_param.x, s.offset_param.y, 0, 0));
                film_mat.SetColor("_PostFilmColor0", s.color0);
                film_mat.SetColor("_PostFilmColor1", s.color1);
                film_mat.SetColor("_PostFilmColor2", s.color2);
                film_mat.SetColor("_PostFilmColor3", s.color3);
                float rad = s.roll_angle * Mathf.Deg2Rad;
                film_mat.SetVector("_PostFilmRollParameter", new Vector4(Mathf.Sin(rad), Mathf.Cos(rad), 0, 0));
                film_mat.SetVector("_PostFilmScaleParameter", new Vector4(s.scale.x, s.scale.y, 0, 0));

                cmd.Blit(cur, nxt, film_mat, 0);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }
        }

        public Shader shader;
        private pass the_pass;

        public override void Create()
        {
            the_pass = new pass(this);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (the_pass == null) the_pass = new pass(this);
            renderer.EnqueuePass(the_pass);
        }
    }
}
