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
                // the ab-test raw frame: every post stage stays bypassed and
                // the camera color passes through untouched, so the snap
                // captured this frame is the scene as the renderer drew it.
                if (ab_raw_frame)
                {
                    ab_raw_frame = false;
                    return;
                }
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

                // the re-enable mask gates each stage independently; the
                // chain starts dark (all features off) and each letter
                // turns its stage back on for the isolation tests.
                string m = feature_mask;
                fade_on &= m.Contains("o");
                fog_on &= m.Contains("o");
                film_on &= m.Contains("f");
                dof_on &= m.Contains("d");
                bloom_on &= m.Contains("b");
                tilt_on &= m.Contains("t");
                grade_on &= m.Contains("g");
                bool radial_on = director.radial_enabled && m.Contains("r");
                bool shafts_on = m.Contains('s') && shafts_lights.pass_enabled
                    && shafts_lights.pass_intensity > 0.001f;
                bool lens_on = (director.lens_distortion_enabled || director.chromatic_enabled) && m.Contains("l");
                bool clarity_on = director.ball_blur_enabled && m.Contains("c");

                if (!fade_on && !fog_on && !film_on && !dof_on && !bloom_on && !tilt_on && !grade_on
                    && !radial_on && !lens_on && !clarity_on) return;

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
                // bloom, then dof, then the film overlay layers. when the
                // dof chain will run, the pyramid builds but the standalone
                // composite skips: the dof pass folds the bloom in itself
                // (the fused DOFBLOOMAPPLYBG shape), and a separate add
                // would double-apply it.
                bool bloom_fused_into_dof = bloom_on && dof_on;
                if (bloom_on)
                {
                    if (bloom_fused_into_dof)
                        apply_bloom(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b, build_only: true);
                    else
                        apply_bloom(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                }
                else
                    last_bloom_pyramid = null;

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
                if (clarity_on)
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
                // exactly the game's PostFilmBlit layer chain. gated by
                // film_on - the mask's film gate - so a single-letter sweep
                // step never runs the layers behind the mask's back (the
                // grade-only step rendered byte-identical to the film-only
                // step because these call sites tested film_valid directly).
                if (film_on && film_valid(director.film1_state))
                    apply_film(cmd, renderingData, director.film1_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                if (film_on && film_valid(director.film2_state))
                    apply_film(cmd, renderingData, director.film2_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);
                if (film_on && film_valid(director.film3_state))
                    apply_film(cmd, renderingData, director.film3_state, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // the color-correction grade rides after the film layers and
                // before the tilt-shift overlay (the game's ColorCorrectionPass
                // runs in its post-bloom chain before the final overlay).
                // gated by grade_on so the mask's letter owns this stage too.
                if (grade_on)
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
                if (radial_on)
                    apply_radial(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // the light shafts ride after radial: bright-pass radial
                // blur toward the Sunshafts pivot, composite 1/r falloff
                // + tint additively (the game's SunShaftsComposite math).
                if (shafts_on)
                    apply_shafts(cmd, renderingData, director, ref cur, ref cur_is_color, ref nxt, ref nxt_is_a, tmp_a, tmp_b);

                // lens distortion rides before the chromatic fringe; chromatic
                // is the chain's last blit (the game draws the fringe at the
                // very end, uv2_postfx_chain_divergence_audit §2.12).
                if (lens_on)
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
                            // effectType = the game material's pass index; our
                            // shader maps chroma to pass 0 (lens = 1) — the
                            // game's type-1 variant is an open decode item.
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
            private int coc_pass = 0;
            private int blurh_pass = 1;
            private int blurv_pass = 2;
            private int comp_pass = 3;
            private int bloom_down_pass = 1;
            private int bloom_blurh_pass = 2;
            private int bloom_blurv_pass = 3;
            private int bloom_comp_pass = 0;
            private Material chroma_mat;
            private Material ballblur_mat;
            private RenderTexture dof_rt_a;
            private RenderTexture dof_rt_b;

            // the game's fastbloom pyramid: three same-res blits at quarter
            // resolution with the threshold folded per level and intensity
            // entering exactly once at the downsample, then a soft-add
            // composite back onto the chain (no composite-time scale).
            //
            // each pyramid stage needs its OWN material: the command buffer
            // resolves a material's properties when the blit EXECUTES, not
            // when it is recorded, so one shared material means the LAST
            // _Parameter write wins for every blit in the chain. the
            // diffusion chain's last write is (blur, blur, 0, 1) — threshold
            // zero, intensity one — so the whole pyramid ran unthresholded
            // and the composite folded the entire scene instead of the
            // hottest highlights (the +57 mean-luminance washout).
            private Material bloom_mat;
            private Material bloom_down_mat;
            private Material bloom_down2_mat;
            private Material bloom_blurh_mat;
            private Material bloom_blurv_mat;
            private Material bloom_diff_down_mat;
            private Material bloom_diff_down2_mat;
            private Material bloom_diff_blurh_mat;
            private Material bloom_diff_blurv_mat;
            private RenderTexture bloom_rt_a;
            private RenderTexture bloom_rt_b;

            // the 1/4-res second pyramid ping-pong target.
            private RenderTexture bloom_rt_c;
            private RenderTexture bloom_rt_diffusion;

            // the diffusion pyramid's own private targets (the game builds
            // its diffusion pyramid from the source; sharing the bloom RTs
            // would clobber the bloom the composite still reads).
            private RenderTexture bloom_rt_d;
            private RenderTexture bloom_rt_e;

            private void apply_bloom(CommandBuffer cmd, RenderingData renderingData,
                                     postfx_director director,
                                     ref RenderTargetIdentifier cur, ref bool cur_is_color,
                                     ref RenderTargetIdentifier nxt, ref bool nxt_is_a,
                                     RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b,
                                     bool build_only = false)
            {
                if (bloom_mat == null)
                {
                    var sh = Shader.Find("live/uv2_bloom");
                    if (sh == null) return;
                    bloom_mat = CoreUtils.CreateEngineMaterial(sh);
                    // name-resolved passes: the same wrong-pass scramble the
                    // dof chain hit (index blits resolving off-target on the
                    // built player). the pyramid's threshold folds silently
                    // no-op'd, leaving the whole scene in the C-fold's bloom.
                    bloom_down_pass = bloom_mat.FindPass("BLOOMDOWN");
                    bloom_blurh_pass = bloom_mat.FindPass("BLOOMBLURH");
                    bloom_blurv_pass = bloom_mat.FindPass("BLOOMBLURV");
                    bloom_comp_pass = bloom_mat.FindPass("BLOOMCOMPOSITE");
                }
                // one material per pyramid stage, so each keeps its own
                // _Parameter: the command buffer reads material properties
                // when the blit executes, and a shared material gives every
                // stage the last value written.
                if (bloom_down_mat == null)
                {
                    bloom_down_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_down2_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_blurh_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_blurv_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_diff_down_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_diff_down2_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_diff_blurh_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                    bloom_diff_blurv_mat = CoreUtils.CreateEngineMaterial(bloom_mat.shader);
                }
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                // the game's pyramid steps 1/2 then 1/4 (CreateBloomTexture
                // blit pairs, fastbloom decode): one box pass per halving.
                // the old shape jumped full->1/4 in a single 4-tap box,
                // undersampling bright lights into hot speckle the blur then
                // smeared into a clipped wash, and ran the neutral pass at
                // 1/4->1/4 (a blur in disguise, not the game's second halving).
                int hw = Mathf.Max(1, desc.width / 2), hh = Mathf.Max(1, desc.height / 2);
                int bw = Mathf.Max(1, desc.width / 4), bh = Mathf.Max(1, desc.height / 4);
                if (bloom_rt_a == null || bloom_rt_a.width != hw || bloom_rt_a.height != hh)
                {
                    if (bloom_rt_a != null) bloom_rt_a.Release();
                    if (bloom_rt_b != null) bloom_rt_b.Release();
                    if (bloom_rt_c != null) bloom_rt_c.Release();
                    if (bloom_rt_diffusion != null) bloom_rt_diffusion.Release();
                    if (bloom_rt_d != null) bloom_rt_d.Release();
                    if (bloom_rt_e != null) bloom_rt_e.Release();
                    bloom_rt_a = new RenderTexture(hw, hh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_b = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_c = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_diffusion = new RenderTexture(bw, bh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_d = new RenderTexture(hw, hh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    bloom_rt_e = new RenderTexture(bw, bh, 0, desc.colorFormat)
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
                // the fork-parity light-shaft approximation: the shafts
                // walker publishes _VolumeLightBloomLift first and the
                // volume walker overwrites it (walker order: shafts, then
                // volume); the effective lift is whichever last published.
                float shaft_lift = volume_uv_scroll.last_lift > 0f
                    ? volume_uv_scroll.last_lift : shafts_lights.last_lift;
                bloom_down_mat.SetVector("_Parameter", new Vector4(
                    1f / desc.width, 1f / desc.height, b.threshold,
                    b.intensity + shaft_lift));
                cmd.Blit(cur, bloom_rt_a, bloom_down_mat, bloom_down_pass);
                // the game's level 1 is a NEUTRAL halving: CreateBloomTexture
                // publishes _Parameter = (uv.x, uv.y, 0, 1.0) for the second
                // downsample (fastbloom disasm @232-233: the threshold slot
                // is zeroed and the intensity slot is 1.0). folding the
                // threshold and intensity again compounded them - the
                // threshold was subtracted twice and the intensity applied
                // twice - which is what crushed the pyramid's mids.
                bloom_down2_mat.SetVector("_Parameter", new Vector4(
                    1f / hw, 1f / hh, 0f, 1f));
                cmd.Blit(bloom_rt_a, bloom_rt_b, bloom_down2_mat, bloom_down_pass);
                // the game's blur order is VERTICAL then HORIZONTAL: the
                // shader file names pass 2 'BlurVertical' and pass 3
                // 'BlurHorizontal' (Gallop_ImageEffects_FastBloom.shader.txt)
                // and CreateBloomTexture blits pass 2 then pass 3.
                // the VERTICAL blur (sp_08) folds the threshold per tap and
                // carries no intensity; the HORIZONTAL blur (sp_10) does not
                // fold and multiplies the accumulated result by the
                // intensity. UV2 had the fold on the horizontal and no final
                // intensity, which left the pyramid too wide and too hot.
                bloom_blurv_mat.SetVector("_Parameter", new Vector4(
                    b.blur_size * Mathf.Pow(2f, -9f) / aspect,
                    b.blur_size * Mathf.Pow(2f, -9f),
                    b.threshold, b.intensity));
                bloom_blurh_mat.SetVector("_Parameter", new Vector4(
                    b.blur_size * Mathf.Pow(2f, -9f) / aspect,
                    b.blur_size * Mathf.Pow(2f, -9f),
                    0f, b.intensity));
                cmd.Blit(bloom_rt_b, bloom_rt_c, bloom_blurv_mat, bloom_blurv_pass);
                cmd.Blit(bloom_rt_c, bloom_rt_b, bloom_blurh_mat, bloom_blurh_pass);
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
                // debug: UV2_BLOOMTEX_DEBUG emits the pyramid the C-fold
                // consumes (BLOOM_DEBUG multi_compile variant).
                {
                    // debug ladder: any non-empty value enables the
                    // composite's BLOOM_DEBUG emit; "1" = level-1 fold
                    // output, "2" = level-2, "0" = the real _BloomTex.
                    var dbg = System.Environment.GetEnvironmentVariable("UV2_BLOOMTEX_DEBUG");
                    if (!string.IsNullOrEmpty(dbg))
                    {
                        bloom_mat.EnableKeyword("BLOOM_DEBUG");
                        if (dbg == "1") bloom_mat.SetTexture("_BloomTex", bloom_rt_d);
                        if (dbg == "2") bloom_mat.SetTexture("_BloomTex", bloom_rt_e);
                        if (dbg == "3") bloom_mat.SetTexture("_BloomTex", bloom_rt_diffusion);
                    }
                }

                // diffusion sub-chain (attribute bit 0x20000): the game's
                // OnRenderImageDiffusionFastBloom (fastbloom_disasm.json,
                // 774 insns) builds CreateBloomTexture's threshold pyramid
                // FROM THE SOURCE and publishes it as _Bloom (0xb7) and
                // _RgbTex (0xac); the composite @54922 samples it as t3.
                // the params it publishes: _bloomDofWeight (0xb3) = key
                // +0x58, _Parameter (0xb8) = (key+0x5c, +0x60, +0x64*0.5,
                // ...), _ColorParam (0xba) = the diffusion row (key
                // +0x70..+0x7c = bright/saturation/contrast/threshold),
                // _BloomIsScreenBlend (0xe1) = 0/1 from key+0x68. the
                // composite's fp row (139.y) is NOT among the published
                // ids, so it binds zero at runtime — p2b2's luma-identity
                // measurement confirms the diffusion texture drops out of
                // the final frame and the composite collapses to
                // max(src, src*src*(2-bloom) + bloom). build the bright-pass
                // pyramid anyway (structural fidelity), and fall back to
                // the base bloom texture when the key's size rounds to zero.
                bool diffusion_on = director.bloom_diffusion_enabled && b.diffusion_blur_size > 0f;
                // the fused dof-bloom composite folds the pyramid as
                // bloom*coc; feeding it the wide 0.1-threshold diffusion
                // pyramid lifts the whole frame by its mean (~0.21 linear)
                // wherever the coc is nonzero — the measured bd-dof delta
                // of +0.36. the game's runtime-zero rows make the diffusion
                // texture drop out (p2b2), so the fused term carries the
                // tight base pyramid only.
                last_bloom_pyramid = bloom_rt_b;
                if (diffusion_on)
                {
                    // the game builds the diffusion pyramid FROM THE SOURCE
                    // (CreateBloomTexture with the diffusion key's params:
                    // threshold downsample, neutral halving, H/V blur at the
                    // diffusion size), not from the bloom texture. the fp
                    // row binds zero at runtime so the texture drops out of
                    // the composite math; the build is structural fidelity.
                    bloom_diff_down_mat.SetVector("_Parameter", new Vector4(
                        1f / desc.width, 1f / desc.height,
                        b.diffusion_threshold, 1f));
                    cmd.Blit(cur, bloom_rt_d, bloom_diff_down_mat, bloom_down_pass);
                    // the game's level 1 is a NEUTRAL halving: the threshold
                    // slot is zeroed and the intensity slot is 1.0
                    // (fastbloom disasm @232-233).
                    bloom_diff_down2_mat.SetVector("_Parameter", new Vector4(
                        1f / hw, 1f / hh, 0f, 1f));
                    cmd.Blit(bloom_rt_d, bloom_rt_e, bloom_diff_down2_mat, bloom_down_pass);
                    // VERTICAL first (folds the threshold per tap, no
                    // intensity), then HORIZONTAL (no fold, multiplies the
                    // intensity) - the game's pass order.
                    bloom_diff_blurv_mat.SetVector("_Parameter", new Vector4(
                        b.diffusion_blur_size * Mathf.Pow(2f, -9f) / aspect,
                        b.diffusion_blur_size * Mathf.Pow(2f, -9f),
                        b.threshold, 1f));
                    bloom_diff_blurh_mat.SetVector("_Parameter", new Vector4(
                        b.diffusion_blur_size * Mathf.Pow(2f, -9f) / aspect,
                        b.diffusion_blur_size * Mathf.Pow(2f, -9f),
                        0f, 1f));
                    cmd.Blit(bloom_rt_e, bloom_rt_c, bloom_diff_blurv_mat, bloom_blurv_pass);
                    cmd.Blit(bloom_rt_c, bloom_rt_diffusion, bloom_diff_blurh_mat, bloom_blurh_pass);
                    bloom_mat.SetTexture("_DiffusionTex", bloom_rt_diffusion);
                    // the composite's fp/lift/lever rows (139.y/151.x/163.w)
                    // are never published by the diffusion C# (its ids are
                    // 179/185/183/172/225/204/182), so they bind zero at
                    // runtime and the texture drops out of the composite
                    // math — the composite collapses to
                    // max(src, src*src*(2-bloom) + bloom), matching the
                    // p2b2 luma-identity measurement. binding a real film
                    // power into fp anyway white-screens the frame (p2b3
                    // run, luma 0.996) — the rows stay zero by design.
                    bloom_mat.SetVector("_DiffusionParams", new Vector4(
                        0f, 0f, 0f, 0f));
                    bloom_mat.SetVector("_DiffusionScreen", new Vector4(
                        0f, 0f, 0f, 0f));
                    // the decoded 151 row in worksheet field order: bright,
                    // saturation, contrast, threshold (uv2_diffusion_composite_
                    // decoded.md + @54922 disasm, key class fields 8-11).
                    bloom_mat.SetVector("_DiffusionCurve", new Vector4(
                        b.diffusion_bright, b.diffusion_saturation,
                        b.diffusion_contrast, b.diffusion_threshold));
                    bloom_mat.SetFloat("_DiffusionBlur", b.diffusion_blur_size);
                    // the diffusion chain republishes _Bloom (id 183) as the
                    // diffusion pyramid — the composite's t2 bloom sample and
                    // t3 diffusion sample both read the 0.1-threshold
                    // source-built texture (fastbloom disasm: CreateBloomTexture
                    // -> GetPropertyId 0xb7 publish at both sites). binding the
                    // hot 0.75-threshold bloom pyramid instead explodes C =
                    // B^2*(2-bloom) + bloom into clipped highlights.
                    // _BloomTex keeps the TIGHT base pyramid: the p2b2
                    // luma-identity measurement says the diffusion texture
                    // drops out of the game's composite (its only lanes are
                    // the fp/lift/lever rows that bind zero), so folding the
                    // wide diffusion blur into the C-term's bloom lifts the
                    // whole frame to the blur's mean (+0.2 at f3300) — the
                    // washout. the diffusion chain's contribution here is
                    // the 151 S-curve row only.
                    bloom_mat.EnableKeyword("DIFFUSION_ON");
                }
                else
                {
                    bloom_mat.SetTexture("_DiffusionTex", bloom_rt_b);
                    bloom_mat.DisableKeyword("DIFFUSION_ON");
                }
                if (build_only)
                {
                    // pyramid built; the dof composite consumes it via
                    // last_bloom_pyramid and folds the add in itself.
                    return;
                }
                cmd.Blit(cur, nxt, bloom_mat, bloom_comp_pass);
                cur = nxt; cur_is_color = false;
                nxt = nxt_is_a ? tmp_b : tmp_a; nxt_is_a = !nxt_is_a;
            }

            // the bloom pyramid this frame's build leaves in its final RT,
            // for the fused dof-bloom composite (uv2_postdofbloom_fused_
            // composite_decoded.md: when the dof chain runs, the game
            // composites bloom INTO the dof pass weighted by coc, not as
            // a separate full-frame add).
            private RenderTexture last_bloom_pyramid;

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
                    // the game finds passes by name (dof_pipeline_decoded:
                    // PASS_POSTDOFBLOOM_APPLYBG etc); index blits on this
                    // material resolved to the wrong pass on the rig (the
                    // H blur read camera-target alpha ~1.0, not the
                    // prefilter's coc), so resolve by name like the game.
                    coc_pass = dof_mat.FindPass("COCPREFILTER");
                    blurh_pass = dof_mat.FindPass("BLURH");
                    blurv_pass = dof_mat.FindPass("BLURV");
                    comp_pass = dof_mat.FindPass("COMPOSITE");
                }
                var cam = renderingData.cameraData.camera;
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                UnityEngine.Debug.Log($"[dof] passes coc={coc_pass} blurh={blurh_pass} blurv={blurv_pass} comp={comp_pass} fmt={desc.colorFormat}");
                if (System.Environment.GetEnvironmentVariable("UV2_DOFCOC_DEBUG") == "1")
                    dof_mat.EnableKeyword("COC_DEBUG");
                if (dof_rt_a == null || dof_rt_a.width != desc.width || dof_rt_a.height != desc.height)
                {
                    if (dof_rt_a != null) dof_rt_a.Release();
                    if (dof_rt_b != null) dof_rt_b.Release();
                    // the coc rides ALPHA through this chain, but the
                    // camera target on this rig is RGB111110Float — no
                    // alpha bits, every coc write is a no-op and the blur
                    // rides alpha 1.0 (the full-frame smear at f3300).
                    // the coc carriers need a real alpha channel.
                    var coc_fmt = RenderTextureFormat.ARGBHalf;
                    dof_rt_a = new RenderTexture(desc.width / 2, desc.height / 2, 0, coc_fmt)
                        { filterMode = FilterMode.Bilinear };
                    dof_rt_b = new RenderTexture(desc.width / 2, desc.height / 2, 0, coc_fmt)
                        { filterMode = FilterMode.Bilinear };
                }

                // the decode (dof_pipeline_decoded.md): _CurveParams =
                // (1, 1, farBlend, offsetY) - the game's two Pow calls with
                // base 1.0 publish 1.0 into x/y (curve left linear; the
                // 1/(focal01*smoothness) claim in the audit doc is wrong at
                // register level); offsetY = invRT.y * aspect.
                dof_mat.SetVector("_DofCurveParams", new Vector4(1f, 1f, director.dof_far_blend,
                    (1f / Mathf.Max(1, desc.height)) * (desc.width / Mathf.Max(1f, desc.height))));
                // fold the §79 coc tail into the scale lane: (spread /
                // aspect) / 512 per defocus meter, matching BlurBlt's
                // register-proven (param_7 / this+0x138) * 0.001953125.
                float dof_aspect = (float)desc.width / Mathf.Max(1, desc.height);
                dof_mat.SetVector("_DofFocusParams", new Vector4(
                    director.dof_focal01,
                    director.dof_blur_spread / (dof_aspect * 512f),
                    director.dof_foreground_size, director.dof_smoothness));
                dof_mat.SetFloat("_DofBlurSpread", director.dof_blur_spread);
                dof_mat.SetVector("_DofFocalMeters", new Vector4(
                    director.dof_focal_m, cam.farClipPlane, desc.height, 0f));

                // the chain runs on its own half-res pair; the composite writes
                // back into the chain's current target.
                cmd.Blit(cur, dof_rt_a, dof_mat, coc_pass);   // coc prefilter
                cmd.Blit(dof_rt_a, dof_rt_b, dof_mat, blurh_pass); // horizontal
                cmd.Blit(dof_rt_b, dof_rt_a, dof_mat, blurv_pass); // vertical
                dof_mat.SetTexture("_DofBlurred", dof_rt_a);
                // the fused dof-bloom composite (DOFBLOOMAPPLYBG @7094,
                // uv2_postdofbloom_fused_composite_decoded.md): when the
                // bloom chain built a pyramid this frame, the game folds
                // the bloom add INTO this pass weighted by the lerped coc
                // — in-focus pixels take no bloom, defocused take full.
                // the filmPower lever (cb0[139].y) and the screen lever
                // (163.w) both bind zero outside the film helper's
                // publish window, so the effective shape is
                // out = lerp(base, tap, cocTap*cocBase) + bloom*coc.
                bool fused_bloom = last_bloom_pyramid != null;
                dof_mat.SetTexture("_DofBloomTex", fused_bloom ? last_bloom_pyramid : dof_rt_a);
                dof_mat.SetFloat("_DofBloomFused", fused_bloom ? 1f : 0f);
                cmd.Blit(cur, nxt, dof_mat, comp_pass);        // composite
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
                // the decoded id-263 remap (film bodies §2): depthClip > 1
                // publishes 0, else 1 - clip. the old 1.5-clip form kept a
                // nonzero gate on clip>1 keys, feeding the depth chain on
                // layers the game renders depthless.
                film_mat.SetFloat("_DepthClip", s.depth_clip > 1f ? 0f : 1f - s.depth_clip);
                // the 152.z zone gate and the IsWithoutDepth kill switch
                // (164.x): the game publishes IsWithoutDepth = IsEnableDepth ?
                // 0 : 1, and IsEnableDepth is a plain bool on the overlay
                // whose only assignment is the constructor default TRUE
                // (the game's own overlay class sets it and never clears
                // it). the game's key data declares no depth-enable field,
                // so the depth term never dies for a film layer. the old
                // gate read an invented 0x200000 attribute bit and killed
                // every film layer on every song, so every vignette-mode
                // layer painted exactly zero. a layer with depthPower 0
                // still paints zero because the layer term is
                // mask * _DepthPower * G.
                film_mat.SetFloat("_DepthClipZone", s.depth_clip > 1f ? 0f : 1f - s.depth_clip);
                film_mat.SetFloat("_PostFilmIsWithoutDepth", 0f);
                film_mat.SetVector("_PostFilmOffsetParam", new Vector4(s.offset_param.x, s.offset_param.y, 0, 0));
                film_mat.SetVector("_PostFilmOptionParam", new Vector4(s.option_param.x, s.option_param.y, 0, 0));
                film_mat.SetColor("_PostFilmColor0", s.color0);
                film_mat.SetColor("_PostFilmColor1", s.color1);
                film_mat.SetColor("_PostFilmColor2", s.color2);
                film_mat.SetColor("_PostFilmColor3", s.color3);
                // the key's colorType selects which authored colors the layer
                // uses: 0 ColorAll (color0 alone), 1 Color2TopBottom,
                // 2 Color2LeftRight, 3 Color4 (the four-corner ramp). the
                // songs author color1..3 as black on colorType-0 keys, so
                // ramping to them faded the layer to black.
                film_mat.SetFloat("_PostFilmColorType", s.color_type);
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

            private Material shafts_mat;
            private RenderTexture shafts_rt_a;
            private RenderTexture shafts_rt_b;

            private void apply_shafts(CommandBuffer cmd, RenderingData renderingData,
                                       postfx_director director,
                                       ref RenderTargetIdentifier cur, ref bool cur_is_color,
                                       ref RenderTargetIdentifier nxt, ref bool nxt_is_a,
                                       RenderTargetIdentifier tmp_a, RenderTargetIdentifier tmp_b)
            {
                if (shafts_mat == null)
                {
                    var sh = Shader.Find("live/uv2_shafts");
                    if (sh == null) return;
                    shafts_mat = CoreUtils.CreateEngineMaterial(sh);
                }
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                int ds = 2;
                int rw = Mathf.Max(1, desc.width / ds), rh = Mathf.Max(1, desc.height / ds);
                if (shafts_rt_a == null || shafts_rt_a.width != rw || shafts_rt_a.height != rh)
                {
                    if (shafts_rt_a != null) shafts_rt_a.Release();
                    if (shafts_rt_b != null) shafts_rt_b.Release();
                    shafts_rt_a = new RenderTexture(rw, rh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                    shafts_rt_b = new RenderTexture(rw, rh, 0, desc.colorFormat)
                        { filterMode = FilterMode.Bilinear };
                }

                // params: intensity from the key alpha, density fixed at the
                // game's sun-shafts default, falloff floor clamps the center.
                shafts_mat.SetVector("_ShaftsParam", new Vector4(
                    shafts_lights.pass_intensity, 0.35f, 0.08f, 0f));
                shafts_mat.SetVector("_ShaftsPivot", shafts_lights.pass_pivot);
                shafts_mat.SetFloat("_ShaftsThreshold", 0.55f);
                shafts_mat.SetVector("_ShaftsTintA", (Vector4)shafts_lights.pass_tint);
                shafts_mat.SetVector("_ShaftsTintB", (Vector4)shafts_lights.pass_tint);
                shafts_mat.SetFloat("_ShaftsBlend", 0f);
                shafts_mat.SetFloat("_ShaftsAspect", (float)desc.width / desc.height);

                // three radial iterations at half-res, then composite add.
                cmd.Blit(cur, shafts_rt_a, shafts_mat, 0);
                cmd.Blit(shafts_rt_a, shafts_rt_b, shafts_mat, 0);
                cmd.Blit(shafts_rt_b, shafts_rt_a, shafts_mat, 0);
                shafts_mat.SetTexture("_ShaftsTex", shafts_rt_a);
                cmd.Blit(cur, nxt, shafts_mat, 1);
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

        // per-feature re-enable mask (UV2_POSTFX_MASK). after the
        // postfx regression pileup the chain now starts fully OFF and
        // each stage comes back one letter at a time so every
        // feature's contribution is measured in isolation against
        // the game. letters: b=bloom, d=dof, f=film, g=grade,
        // t=tilt, o=fog+fade, r=radial, l=lens+chromatic, c=clarity
        // (ball blur). the string "bd" runs ONLY bloom and dof.
        // empty or unset = the whole chain bypassed (raw render).
        // phase re-enable (10-20): bloom is back on by default and the env
        // override works again, per the documented reopen path from the
        // retirement commit. the stages land one letter at a time, each
        // gated by a rig-vs-official comparison before the next letter
        // defaults on; the rest stay dark until their phase passes.
        // phase 2 (10-20): dof joins — the director already dispatches the
        // game's per-key dofBlurType chains (0/3 run no dof blur, blur-bearing
        // types 1/2/4/5/6 do), so the letter re-arms exactly the game's
        // authored selection.
        // phase 3 (10-20): the film overlay layers join (1004 authors 557
        // film keys; film2's vignette-add inverse-power draws the monitor
        // quads and the light-wall and the border darkening the official
        // carries). phase 2b folded the decoded diffusion composite tail
        // (the 151 curve row + C-subtract) into the bloom composite.
        // phase 4 (10-20): lens distortion + chromatic aberration join —
        // 1004 authors 28 chromatic keys; the shared chromatic_lens pass
        // rides before the composite chain per the audit order.
        // phase 5 (10-20): tilt-shift lens blur (1004's authored m1 a5.6
        // blur25 roll90 keys) and the color grade join the chain.
        // phase 6 (10-20): ball blur joins — 1004 and 1151 are the only
        // two songs that author it (clarity pass, letter c).
        // phase 7 (10-20): radial blur and fog/fade complete the mask —
        // every authored family now rides its letter; the director's
        // per-key validity keeps inert families no-ops (1004 authors no
        // fog, its radial keys gate off at the snapped moments).
        public static string feature_mask =
            // the shafts letter ('s') stays OFF by default: the screen-space
            // radial approximation smears the whole frame (the game's real
            // shafts composite 4 beam-geometry RTs, not a scene blur). the
            // pass stays behind UV2_POSTFX_MASK=s for the geometry route.
            // the whole chain is off by default (10-24): the official-video
            // frame comparison needs the raw render, because the post chain
            // changes the exposure and the bloom far more than it changes
            // the content, and a brightness delta against the official frame
            // says nothing about the pose or the camera. the chain comes
            // back one letter at a time behind UV2_POSTFX_MASK.
            System.Environment.GetEnvironmentVariable("UV2_POSTFX_MASK") ?? "";

        // ab-test switch for the snapshot pairs: the director's snap hook
        // raises this for one frame so the same song point renders raw
        // (every post stage bypassed) right after the full-chain capture.
        // reading it at Execute time - not AddRenderPasses - because unity
        // calls AddRenderPasses before the director's tick for the frame.
        public static bool ab_raw_frame = false;

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
