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
                bool tilt_on = director.tilt_enabled;
                bool grade_on = director.cc != null && director.cc.valid && director.cc_lut != null;
                if (!fade_on && !fog_on && !film_on && !dof_on && !bloom_on && !tilt_on && !grade_on
                    && !director.radial_enabled
                    && !director.lens_distortion_enabled && !director.chromatic_enabled
                    && !director.ball_blur_enabled) return;

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
                        // the game's _SceneFogParams per the consumer decode:
                        // .x = exp2 density (mode 3), .y = exp density (mode
                        // 2), .z/.w = the linear ramp (mode 1). the shader muxes
                        // on _SceneFogMode.x == 1/2/3.
                        if (f.fog_mode == 1)
                            mat.SetVector("_SceneFogParams", new Vector4(
                                0f, 0f,
                                1f / Mathf.Max(0.0001f, f.end - f.start),
                                -f.start / Mathf.Max(0.0001f, f.end - f.start)));
                        else if (f.fog_mode == 2)
                            mat.SetVector("_SceneFogParams", new Vector4(
                                0f, f.exp_density * 1.4427f, 0f, 0f));
                        else
                            mat.SetVector("_SceneFogParams", new Vector4(
                                f.exp_density * 1.20112f, 0f, 0f, 0f));
                        mat.SetVector("_SceneFogMode", new Vector4(
                            (float)f.fog_mode,
                            f.use_radial_distance != 0 ? 1f : 0f, 0, 0));
                        mat.SetFloat("_HeightDensity", f.height_density);
                        mat.SetFloat("_FogStart", f.start);
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

                // ball blur (the dof key's own family, 1004/1151): the
                // extraction/spread/add halo after the dof composite.
                if (director.ball_blur_enabled)
                {
                    if (ballblur_mat == null)
                    {
                        var sh = Shader.Find("live/uv2_ballblur");
                        if (sh != null) ballblur_mat = CoreUtils.CreateEngineMaterial(sh);
                    }
                    if (ballblur_mat != null)
                    {
                        ballblur_mat.SetVector("_BallBlurParams", new Vector4(
                            director.ball_blur_power, director.ball_blur_threshold,
                            director.ball_blur_intensity, director.ball_blur_spread));
                        cmd.Blit(cur, nxt, ballblur_mat, 0);
                        cur = nxt; cur_is_color = false;
                        nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
                    }
                }

                // the film overlays: each valid layer blits on top in order,
                // exactly the game's PostFilmBlit layer chain.
                if (film_valid(director.film1_state))
                    apply_film(cmd, renderingData, director.film1_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                if (film_valid(director.film2_state))
                    apply_film(cmd, renderingData, director.film2_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                if (film_valid(director.film3_state))
                    apply_film(cmd, renderingData, director.film3_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // the color-correction grade rides after the film layers and
                // before the tilt-shift overlay (the game's ColorCorrectionPass
                // runs in its post-bloom chain before the final overlay).
                if (director.cc != null && director.cc.valid && director.cc_lut != null)
                    apply_colorgrade(cmd, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // the tilt-shift overlay runs last — the game's Execute flow
                // ends the chain with it (final: event<=550 source-rt else
                // plain copy; the visible output IS the blurred image).
                if (tilt_on)
                    apply_tilt(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // the radial blur chain rides after tilt-shift: downsample to
                // two temps, ping-pong the blur pass iteration times, then the
                // composite pass folds _BlurTex back onto the chain (the
                // game's RadialBlurPass::Execute blit order).
                if (director.radial_enabled)
                    apply_radial(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // lens distortion rides before the chromatic fringe; chromatic
                // is the chain's last blit (the game draws the fringe at the
                // very end, uv2_postfx_chain_divergence_audit §2.12).
                if (director.lens_distortion_enabled || director.chromatic_enabled)
                {
                    if (chroma_mat == null)
                    {
                        var sh = Shader.Find("live/uv2_chromatic_lens");
                        if (sh != null) chroma_mat = CoreUtils.CreateEngineMaterial(sh);
                    }
                    if (chroma_mat != null)
                    {
                        if (director.lens_distortion_enabled)
                        {
                            chroma_mat.SetFloat("_LensIntensity", director.lens_intensity);
                            chroma_mat.SetVector("_LensCenter",
                                new Vector4(director.lens_center_x, director.lens_center_y, 0, 0));
                            chroma_mat.SetFloat("_LensScale",
                                director.lens_scale != 0f ? director.lens_scale : 1f);
                            cmd.Blit(cur, nxt, chroma_mat, 1);
                            cur = nxt; cur_is_color = false;
                            nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
                        }
                        if (director.chromatic_enabled)
                        {
                            chroma_mat.SetFloat("_ChromaAmount", director.chromatic_amount);
                            var ck = director.chromatic_key_offsets;
                            chroma_mat.SetFloat("_ChromaClip", ck.clip);
                            chroma_mat.SetVector("_ChromaR", new Vector4(ck.red.x, ck.red.y, 0, 0));
                            chroma_mat.SetVector("_ChromaG", new Vector4(ck.green.x, ck.green.y, 0, 0));
                            chroma_mat.SetVector("_ChromaB", new Vector4(ck.blue.x, ck.blue.y, 0, 0));
                            cmd.Blit(cur, nxt, chroma_mat, 0);
                            cur = nxt; cur_is_color = false;
                            nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
                        }
                    }
                }

                if (!cur_is_color)
                    cmd.Blit(cur, color);

                cmd.ReleaseTemporaryRT(tmp_a_id);
                cmd.ReleaseTemporaryRT(tmp_b_id);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                // snapshot the final frame at a few song points so the llvmpipe
                // bench leaves a PNG we can inspect. blocking ReadPixels against
                // the camera's color target — slow but we only fire 3 frames.
                int song_frame = (int)Mathf.RoundToInt(director.clock.time * 60f);
                if ((song_frame >= 1780 && song_frame <= 1810) || (song_frame >= 3580 && song_frame <= 3610))
                {
                    Debug.Log($"snap probe: f={song_frame} t={director.clock.time:0.0}");
                    if (snap_fired != song_frame)
                    {
                        snap_fired = song_frame;
                        Debug.Log($"snap: capturing frame {song_frame} t={director.clock.time:0.00}");
                        var rt = RenderTexture.GetTemporary(
                            renderingData.cameraData.cameraTargetDescriptor.width,
                            renderingData.cameraData.cameraTargetDescriptor.height,
                            24);
                        var snap_cmd = CommandBufferPool.Get("uv2_snap");
                        snap_cmd.Blit(color, rt);
                        context.ExecuteCommandBuffer(snap_cmd);
                        CommandBufferPool.Release(snap_cmd);
                        RenderTexture.active = rt;
                        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                        tex.Apply(false, false);
                        RenderTexture.active = null;
                        RenderTexture.ReleaseTemporary(rt);
                        var bytes = ImageConversion.EncodeToPNG(tex);
                        UnityEngine.Object.Destroy(tex);
                        var snap_dir = "/work/UmaViewer2/Logs";
                        System.IO.Directory.CreateDirectory(snap_dir);
                        var path = $"{snap_dir}/uv2_snap_f{song_frame}_t{director.clock.time:0.0}.png";
                        System.IO.File.WriteAllBytes(path, bytes);
                        Debug.Log($"snap wrote {path}");
                    }
                }
            }

            private int snap_fired = -1;

            private static readonly int tmp_a_id = Shader.PropertyToID("_uv2_postfx_tmp_a");
            private static readonly int tmp_b_id = Shader.PropertyToID("_uv2_postfx_tmp_b");

            private Material dof_mat;
            private Material chroma_mat;
            private Material ballblur_mat;
            private RenderTexture dof_rt_a;
            private RenderTexture dof_rt_b;

            // the game's fastbloom pyramid: three same-res blits at quarter
            // resolution with the threshold folded per level and intensity
            // entering exactly once at the downsample, then a soft-add
            // composite back onto the chain (no composite-time scale).
            private Material bloom_mat;
            private RenderTexture bloom_rt_a;
            private RenderTexture bloom_rt_b;
            private RenderTexture bloom_rt_diffusion;

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
                    if (bloom_rt_diffusion != null) bloom_rt_diffusion.Release();
                    bloom_rt_a = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_b = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_diffusion = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                }

                var b = director.bloom_state;
                float aspect = (float)desc.width / Mathf.Max(1, desc.height);
                // the game's publishes, register-proven (fastbloom decode):
                // blit A (pass 1 DownSample): _Parameter = (1/srcW, 1/srcH,
                // threshold, intensity); blits C/D (passes 2+3):
                // (blur*2^-9/aspect, blur*2^-9, threshold, intensity).
                // intensity folds at the downsample only; the blur passes
                // fold the threshold per tap and carry no intensity scale.
                // the game's CreateBloomTexture (fastbloom_disasm.json
                // insns 381-442): downsample(1) -> downsample-neutral(1 with
                // _Parameter.z=0, w=1) -> blurH(2) -> blurV(3), and the
                // composite binds the pass-3 RT. UV2 previously skipped the
                // neutral second downsample (bloom read smaller and hotter)
                // and ran the blurs V-then-H.
                bloom_mat.SetVector("_Parameter", new Vector4(
                    1f / desc.width, 1f / desc.height, b.threshold, b.intensity));
                cmd.Blit(cur, bloom_rt_a, bloom_mat, 1);
                // neutral second downsample: threshold 0, intensity 1.
                bloom_mat.SetVector("_Parameter", new Vector4(
                    1f / bw, 1f / bh, 0f, 1f));
                cmd.Blit(bloom_rt_a, bloom_rt_b, bloom_mat, 1);
                bloom_mat.SetVector("_Parameter", new Vector4(
                    b.blur_size * Mathf.Pow(2f, -9f) / aspect,
                    b.blur_size * Mathf.Pow(2f, -9f),
                    b.threshold, b.intensity));
                cmd.Blit(bloom_rt_b, bloom_rt_a, bloom_mat, 2);
                cmd.Blit(bloom_rt_a, bloom_rt_b, bloom_mat, 3);
                // composite (pass 0 Bloom): additive for authored mode 1, the
                // screen-blend family otherwise; never an intensity scale.
                // the game publishes _bloomDofWeight (id 179) and
                // _BloomIsScreenBlend (id 225, 0 for mode-1 Add) before the
                // PostFilmBlit composite (fastbloom decode).
                bloom_mat.SetTexture("_BloomTex", bloom_rt_b);
                bloom_mat.SetFloat("_BloomBlendMode", b.blend_mode == 1 ? 1f : 0f);
                bloom_mat.SetFloat("_bloomDofWeight", b.bloom_dof_weight);
                bloom_mat.SetFloat("_BloomIsScreenBlend", b.blend_mode == 1 ? 0f : 1f);
                // the composite's shaping row cb0[139].y = _PostFilmPower =
                // the live film layer's authored power (draw helper id192,
                // fog_fade publish line ~545). the game scales the source by
                // filmPower before adding bloom (@51362 r2 = t2*139.y + r4);
                // with no valid film layer the composite path is the plain
                // bloom variant and the source passes unscaled, so gate on
                // film validity the same way.
                bool film_shapes_src = director.film1_state != null && director.film1_state.valid;
                float shaping = film_shapes_src ? director.film1_state.power : 1f;
                bloom_mat.SetFloat("_BloomShaping", shaping);

                // diffusion sub-chain (Director.cs:1307, attribute bit
                // 0x20000): the game's PostDiffusionBloom_Rich path runs a
                // SECOND blur pyramid at the key's diffusion_blur_size, and
                // the composite grades THAT texture. we build the wider
                // pyramid from bloom_rt_b (the tight bloom) with its own H/V
                // blur at the diffusion size, and fall back to the base bloom
                // texture when the key's size rounds to zero.
                bool diffusion_on = director.bloom_diffusion_enabled && b.diffusion_blur_size > 0f;
                if (diffusion_on)
                {
                    bloom_mat.SetVector("_Parameter", new Vector4(
                        b.diffusion_blur_size * Mathf.Pow(2f, -9f) / aspect,
                        b.diffusion_blur_size * Mathf.Pow(2f, -9f),
                        0f, 1f));
                    // H then V into its own rt so the base bloom stays intact
                    // for the composite's tight pass.
                    cmd.Blit(bloom_rt_b, bloom_rt_a, bloom_mat, 2);
                    cmd.Blit(bloom_rt_a, bloom_rt_diffusion, bloom_mat, 3);
                    bloom_mat.SetTexture("_DiffusionTex", bloom_rt_diffusion);
                    bloom_mat.SetVector("_DiffusionParams", new Vector4(
                        b.diffusion_threshold, b.diffusion_bright,
                        b.diffusion_saturation, b.diffusion_contrast));
                    bloom_mat.SetFloat("_DiffusionBlur", b.diffusion_blur_size);
                    bloom_mat.EnableKeyword("DIFFUSION_ON");
                }
                else
                {
                    bloom_mat.SetTexture("_DiffusionTex", bloom_rt_b);
                    bloom_mat.DisableKeyword("DIFFUSION_ON");
                }
                cmd.Blit(cur, nxt, bloom_mat, 0);
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

                // the decode (dof_pipeline_decoded.md): _CurveParams =
                // (1, 1, farBlend, offsetY) - the game's two Pow calls with
                // base 1.0 publish 1.0 into x/y (curve left linear; the
                // 1/(focal01*smoothness) claim in the audit doc is wrong at
                // register level); offsetY = invRT.y * aspect.
                dof_mat.SetVector("_DofCurveParams", new Vector4(1f, 1f, director.dof_far_blend,
                    (1f / Mathf.Max(1, desc.height)) * (desc.width / Mathf.Max(1f, desc.height))));
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

            private Material tilt_mat;

            // the game's TiltShiftHdrLensBlur, ported per the decode doc: one
            // full-res blit with pass = quality*2 + (mode!=1) — planar or
            // radial COC by mode, blur radius min(|dist|*area, size) planar /
            // clamp(dot(d,d)*area, 0, size) radial, 28-tap jittered disc,
            // _Params = (offset.x, offset.y, sin(roll), cos(roll)).
            private Material grade_mat;

            // the color-correction grade: sample the director's 256x1 rgb LUT
            // per channel, then the authored saturation.
            private void apply_colorgrade(CommandBuffer cmd, postfx_director director,
                                          ref RenderTargetIdentifier cur, ref bool cur_is_color,
                                          ref RenderTargetIdentifier nxt, ref bool nxt_is_a,
                                          RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (grade_mat == null)
                {
                    var sh = Shader.Find("live/uv2_colorgrade");
                    if (sh == null) return;
                    grade_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                grade_mat.SetTexture("_Lut", director.cc_lut);
                grade_mat.SetFloat("_Saturation", director.cc.saturation);
                cmd.Blit(cur, nxt, grade_mat, 0);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }

            private void apply_tilt(CommandBuffer cmd, RenderingData renderingData,
                                    postfx_director director, ref RenderTargetIdentifier cur,
                                    ref bool cur_is_color, ref RenderTargetIdentifier nxt,
                                    ref bool nxt_is_a, RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (tilt_mat == null)
                {
                    var sh = Shader.Find("live/uv2_tiltshift");
                    if (sh == null) return;
                    tilt_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                tilt_mat.SetVector("_MainTex_TexelSize", new Vector4(
                    1f / Mathf.Max(1, desc.width), 1f / Mathf.Max(1, desc.height),
                    desc.width, desc.height));
                tilt_mat.SetFloat("_BlurSize", director.tilt_max_blur);
                tilt_mat.SetFloat("_BlurArea", director.tilt_blur_area);
                float rad = director.tilt_roll * Mathf.Deg2Rad;
                tilt_mat.SetVector("_Params", new Vector4(
                    director.tilt_offset.x, director.tilt_offset.y,
                    Mathf.Sin(rad), Mathf.Cos(rad)));
                cmd.Blit(cur, nxt, tilt_mat, director.tilt_pass);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }

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
                // the game's draw helper publishes id192 _PostFilmPower =
                // param+0x04 = key.filmPower and id187 _DepthPower = param+0x08
                // = key.depthPower (register-proven at 0x7ff8e513707f/70ad -
                // the fork's names are correct, there is no swap). the ps
                // scales every mode's layer color by _DepthPower; the validity
                // gate tests filmPower. (uv2_film_mode_bodies_decoded.md §5-6)
                film_mat.SetFloat("_PostFilmPower", s.power);
                film_mat.SetFloat("_DepthPower", s.depth_power);
                // the game publishes max(0, 1.5 - clip) into _DepthClip
                // (composite decode, id 263).
                film_mat.SetFloat("_DepthClip", Mathf.Max(0f, 1.5f - s.depth_clip));
                film_mat.SetVector("_PostFilmOffsetParam", new Vector4(s.offset_param.x, s.offset_param.y, 0, 0));
                film_mat.SetVector("_PostFilmOptionParam", new Vector4(s.option_param.x, s.option_param.y, 0, 0));
                film_mat.SetColor("_PostFilmColor0", s.color0);
                film_mat.SetColor("_PostFilmColor1", s.color1);
                film_mat.SetColor("_PostFilmColor2", s.color2);
                film_mat.SetColor("_PostFilmColor3", s.color3);
                // the game's packing (ScreenOverlayRender Render.Blit):
                // roll = (sin, cos, width/height aspect, 1) and scale =
                // (1/scale.x, 1/scale.y, 0, 0) reciprocals - the audit's
                // film uv-warp divergence (§2.4): UV2 previously published
                // (sin, cos, 0, 0) and raw scale, so the warp was wrong.
                float rad = s.roll_angle * Mathf.Deg2Rad;
                var fd = renderingData.cameraData.cameraTargetDescriptor;
                float aspect = (float)fd.width / Mathf.Max(1, fd.height);
                film_mat.SetVector("_PostFilmRollParameter", new Vector4(Mathf.Sin(rad), Mathf.Cos(rad), aspect, 1f));
                film_mat.SetVector("_PostFilmScaleParameter", new Vector4(
                    s.scale.x != 0f ? 1f / s.scale.x : 1f,
                    s.scale.y != 0f ? 1f / s.scale.y : 1f,
                    0f, 0f));
                // the game's draw helper publishes isUseTexMask as
                // _PostFilmIsInverseVignette (id 223) - the shader's mask
                // inversion flag. ignoring it made every authored inverse
                // layer paint the uninverted full mask (the white wash).
                film_mat.SetFloat("_PostFilmIsInverseVignette", s.inverse ? 1f : 0f);

                cmd.Blit(cur, nxt, film_mat, 0);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }
            // the game's radial blur chain (RadialBlurPass::Execute blit order,
            // uv2_radialblur_decoded.md): downsample the chain into two temps at
            // size/downsample, run the blur pass ping-pong for iteration counts,
            // then the composite pass mixes _BlurTex back by the area/depth
            // factor. pass = 2*(type-1), composite = pass+1.
            private Material radial_mat;
            private RenderTexture radial_rt_a;
            private RenderTexture radial_rt_b;

            private void apply_radial(CommandBuffer cmd, RenderingData renderingData,
                                       postfx_director director,
                                       ref RenderTargetIdentifier cur, ref bool cur_is_color,
                                       ref RenderTargetIdentifier nxt, ref bool nxt_is_a,
                                       RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (radial_mat == null)
                {
                    var sh = Shader.Find("live/uv2_radialblur");
                    if (sh == null) return;
                    radial_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                int ds = Mathf.Max(1, director.radial_downsample);
                int rw = Mathf.Max(1, desc.width / ds), rh = Mathf.Max(1, desc.height / ds);
                if (radial_rt_a == null || radial_rt_a.width != rw || radial_rt_a.height != rh)
                {
                    if (radial_rt_a != null) radial_rt_a.Release();
                    if (radial_rt_b != null) radial_rt_b.Release();
                    radial_rt_a = new RenderTexture(rw, rh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    radial_rt_b = new RenderTexture(rw, rh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                }

                // the publishes: _BlurParam/_BlurParamEx carry the cb rows 139/140.
                radial_mat.SetVector("_BlurParam", director.radial_blur_param);
                radial_mat.SetVector("_BlurParamEx", director.radial_blur_param_ex);
                radial_mat.SetFloat("_BlurEndArea", director.radial_end_area);
                radial_mat.SetVector("_DepthCancelRect", director.radial_cancel_rect);
                radial_mat.SetFloat("_DepthCancelBlendLength",
                    Mathf.Max(1e-5f, director.radial_blend_length));
                // jitter seed: the game scrolls the uv by the global param each
                // frame; the worksheet keys never author it, so keep zero.
                radial_mat.SetVector("_GlobalScreenUVScrollParam", Vector4.zero);
                // per-axis step scale: the game's UnityPerMaterial row carries the
                // camera's texel scale; on the downsampled rt the blur step is
                // relative to the source texel.
                radial_mat.SetVector("_RadialBlurAxisScale",
                    new Vector4(1f / rw, 1f / rh, 0f, 0f));
                radial_mat.SetFloat("_RadialBlurSampleBias", -1f);

                int blur_pass = 2 * (director.radial_type - 1);
                // depth + rect gates only exist on the type 2 slot in the game's
                // pass table; the others author them zero anyway.
                cmd.Blit(cur, radial_rt_a, radial_mat, blur_pass);
                var src = radial_rt_a;
                var dst = radial_rt_b;
                int iterations = Mathf.Max(1, director.radial_iteration);
                for (int i = 1; i < iterations; i++)
                {
                    cmd.Blit(src, dst, radial_mat, blur_pass);
                    var swap = src; src = dst; dst = swap;
                }
                radial_mat.SetTexture("_BlurTex", src);
                cmd.Blit(cur, nxt, radial_mat, blur_pass + 1);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }


        }

        public Shader shader;
        private pass the_pass;
        private light_globals_pass the_light_pass;

        public override void Create()
        {
            the_pass = new pass(this);
        }

        // republishes the game's chara-light globals inside the render loop.
        // urp's ForwardLights::SetupMainLightConstants publishes
        // _MainLightColor = finalColor (color*intensity) of the main
        // directional at SetupLights — our rig's sun is intensity 0 and the
        // zero-light default is Color.black — and that lands AFTER every c#
        // publish, so c# SetGlobalColor can never win. the game's chara
        // shader multiplies albedo/toon by cb0[7] _MainLightColor, so the
        // clobber renders chars black on real gpus (llvmpipe binds a stripped
        // variant without the multiply, which is why the bench looked fine).
        // this pass rides at BeforeRendering, after SetupLights, so the last
        // write is the game's value. (uv2_chara_toon_light_decoded.md,
        // uv2_chara_texture_color_decoded.md)
        private class light_globals_pass : ScriptableRenderPass
        {
            private static readonly int id_main_light_pos = Shader.PropertyToID("_MainLightPosition");
            private static readonly int id_main_light_color = Shader.PropertyToID("_MainLightColor");

            // the stage loader feeds these every frame; static fallbacks keep
            // the pass safe before the loader opens (menus, shutdown).
            public static Vector3 light_dir = Vector3.down;

            public light_globals_pass()
            {
                renderPassEvent = RenderPassEvent.BeforeRendering;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // only the two urp-clobbered names need the republish; the
                // fog globals are gallop-custom and urp never touches them,
                // so republishing fog-off here would clobber authored fog
                // keys on songs that use fog.
                var d = light_dir;
                CommandBuffer cmd = CommandBufferPool.Get("uv2 chara light globals");
                cmd.SetGlobalVector(id_main_light_pos, new Vector4(d.x, d.y, d.z, 0f));
                cmd.SetGlobalColor(id_main_light_color, Color.white);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        // the light-globals republish pass rides before every camera render.
        public static void set_main_light_dir(Vector3 dir)
            => light_globals_pass.light_dir = dir;

        // bisect switch for the white-frame hunt: an env var disables the
        // whole post chain so the renderer can be isolated from the chain.
        public static bool chain_disabled = System.Environment.GetEnvironmentVariable("UV2_NO_POSTFX") == "1";

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (chain_disabled) return;
            if (the_pass == null) the_pass = new pass(this);
            renderer.EnqueuePass(the_pass);
            // the light-globals republish rides before every camera render.
            if (the_light_pass == null) the_light_pass = new light_globals_pass();
            renderer.EnqueuePass(the_light_pass);
        }
    }
}
