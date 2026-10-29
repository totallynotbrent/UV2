// the game's FastBloom pyramid, register-exact per the d3dasm decode of the
// real DXBC subprograms (out/fastbloom_dxbc/sp_03/05/08/10 + pass table):
//   pass 0 "Bloom":          2-texture 3-mode composite; authored 1004 keys
//     use BloomBlendMode 1 = additive (src + bloom), no intensity multiply.
//   pass 1 "DownSample":     4-tap 2x2 box average, then threshold SUBTRACTS
//     once and intensity multiplies once: max(avg - z, 0) * w.
//   pass 2 "BlurVertical":   9 taps at {0,1,2,3,5}*step.y, weights
//     {0.225, 0.15, 0.11, 0.075, 0.0525} (sum exactly 1.0), per-tap fold
//     max(s - z, 0), no intensity multiply.
//   pass 3 "BlurHorizontal": same taps on x, no fold, intensity ONCE at the
//     end: o = weighted * w.
// _Parameter: (texel x, texel y, threshold, intensity) — texel sizes are the
// SOURCE reciprocals on the downsample, blur*2^-9 family on the blur passes.
Shader "live/uv2_bloom"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
        // engine-material SetVector targets must appear in the property
        // sheet or the write silently no-ops: the pyramid's _Parameter
        // (threshold/intensity folds) never bound, leaving the bloom
        // texture as the raw quarter-res scene (the +0.19 washout).
        _Parameter ("FastBloomParams", Vector) = (0.001, 0.001, 0.15, 0.7)
        _BloomBlendMode ("BlendMode", Float) = 1
        _bloomDofWeight ("DofWeight", Float) = 0.75
        _BloomIsScreenBlend ("IsScreenBlend", Float) = 0
        _BloomShaping ("Shaping", Float) = 1
        _DiffusionBlur ("DiffusionBlur", Float) = 1.5
        _BloomTex ("BloomTex", 2D) = "" {}
        _DiffusionTex ("DiffusionTex", 2D) = "" {}
        _DiffusionCurve ("DiffusionCurve", Vector) = (1, 0.9, 1.1, 0.1)
        _DiffusionParams ("DiffusionParams", Vector) = (0, 0, 0, 0)
        _DiffusionScreen ("DiffusionScreen", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // pass 0: the composite. mode 1 = additive (the authored mode for
        // 1004); mode 0 = screen blend family for other songs. the
        // DIFFUSION_ON variant mixes the game's PostDiffusionBloom_Rich
        // shape: bright-pass blur with saturation/contrast grading.
        Pass
        {
            Name "BLOOMCOMPOSITE" 
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_composite
            #pragma multi_compile _ DIFFUSION_ON
            #pragma multi_compile _ BLOOM_DEBUG
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            sampler2D _DiffusionTex;
            float _BloomBlendMode;   // authored BloomBlendMode: 1 = additive
            float _bloomDofWeight;   // the game publishes id 179 pre-composite
            float _BloomIsScreenBlend;  // id 225: 0 for the Add family
            float4 _DiffusionParams;  // (lift, -, -, fp)
            float4 _DiffusionScreen;  // (-, -, -, lever: add vs screen)
            float4 _DiffusionCurve;   // (bright, saturation, contrast, threshold)
            float _DiffusionBlur;
            float _BloomShaping;   // the composite's shaping constant row

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float4 frag_composite(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float4 bloom = tex2D(_BloomTex, i.uv);
                #if BLOOM_DEBUG
                // debug: emit the bloom pyramid the composite folds —
                // the rig reads the actual C-term input.
                return float4(bloom.rgb, 1);
                #endif
                // the game's film-variant combine @51362 with the runtime
                // unbound constants (cb0[139] = cb0[163] = 0, verified in
                // postbloom_composite_pass0_math.md): r0 = warped_src -
                // bloom, then out = r0*r0 + bloom, all * _DimmerColor.
                // the (src-bloom)^2 fold COMPRESSES the highlight: a hot
                // source over hot bloom lands at (0.6)^2+0.3 = 0.66, never
                // clipping, where a plain additive saturates to a white
                // wash. that fold is the game's overexposure guard.
                float4 col = (saturate(src) - bloom) * (saturate(src) - bloom) + bloom;

#ifdef DIFFUSION_ON
                // the game's diffusion composite (PostDiffusionBloom_Rich
                // pass-0 PS @54922, uv2_diffusion_composite_decoded.md):
                //   A = src + diff*fp
                //   B = src + diff*fp*(1 - lever*src)   (add vs screen lever)
                //   C = B*B*(2-bloom) + bloom + lift
                //   out = max(B, max(C, 0))
                // the square compresses the diffused energy and the (2-bloom)
                // screen term adds bloom; the max against B never lets the
                // result fall under the linear base.
                // fp/lever/lift are rows 139.y/163.w/151.x - NOT runtime
                // published by the diffusion C# (it only publishes ids
                // 179/185/183/172/225/204/182) and NOT material-facing props
                // (catalog: only _MainTex/_DimmerColor), so at runtime they
                // bind ZERO. with fp=0 the form collapses to
                // out = max(src, src*src*(2-bloom) + bloom) - bloom only lifts
                // pixels where it beats the source, which is the game's
                // subtle glow. the earlier fp=1/lift=0.1/lever=1 guesses made
                // this a full screen-blend toward the diffusion texture plus
                // a square lift - the +0.23 mean-luminance bloom-step residual
                // measured in bench_20261010_1611. there is also NO filmPower
                // multiply on the source in this variant (@54922 samples t1
                // straight), so the shaping row does not apply here - only in
                // the non-diffusion postbloom composite (@51362).
                float4 diff = tex2D(_DiffusionTex, i.uv);
                float3 B = src.rgb + diff.rgb * _DiffusionParams.w * (1.0 - _DiffusionScreen.w * src.rgb);
                // the decoded tail @54922: C = B^2*(2-bloom) + bloom - 151.w,
                // max(C,0), max(B,C), then the 151.xyz luma S-curve
                // (saturation x, chroma gain y, contrast z, pivot 0.5).
                // the decoded tail @54922 ops 22-25: r3 = B^2 + bloom,
                // r1 = -bloom*B^2 + r3 = B^2*(1-bloom) + bloom — a screen
                // blend of B^2 with bloom, NOT (2-bloom); the (2-bloom)
                // reading over-lifted highlights into clip. the +cb0[151].x
                // (_ColorPower.x) row is not among the diffusion chain's
                // published ids (179/185/183/172/225/204/182) so it binds
                // zero at runtime, same doctrine as the fp/lift/lever rows.
                // the B^2*(1-bloom)+bloom reading lifted the frame
                // +0.30 vs the official landmark (54.300s): the game's
                // rows that weight this term bind zero at runtime
                // (p2b2 luma-identity), so the C term collapses to the
                // bloom texture alone and the composite keeps only
                // max(src, bloom) + the 151 S-curve.
                float3 C = bloom.rgb;
                float3 m = max(B, max(C, 0.0));
                // the 151 row in worksheet field order: x bright (pre-scale),
                // y saturation (chroma gain), z contrast (pivot-0.5 S), w
                // diffusion threshold (the C subtract). 1004 authors
                // (1.0, 0.9, 1.1, 0.1) — mild desat, slight contrast, small
                // highlight soften.
                float3 sm = m * _DiffusionCurve.x;
                float lum = dot(sm, float3(0.2125, 0.7154, 0.0721));
                float3 ch = sm - lum;
                ch = _DiffusionCurve.y * ch + lum;
                ch = (ch - 0.5) * _DiffusionCurve.z + 0.5;
                col.rgb = ch;
#endif
                return col;
            }
            ENDCG
        }

        // pass 1: the 4-tap downsample — 2x2 neighborhood, threshold fold
        // once, intensity once (the only fold the downsample does).
        Pass
        {
            Name "BLOOMDOWN"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_down
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Parameter;   // (1/srcW, 1/srcH, threshold, intensity)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float4 frag_down(v2f i) : SV_Target
            {
                float2 tx = _Parameter.xy;
                // the game's downsample (fastbloom sp_00) averages EXACTLY
                // FOUR taps and scales by 0.25: the four uv corners
                // (v1.xy, v1.zw, v2.xy, v2.zw) the vertex stage emits. UV2
                // sampled the centre plus four corners — five taps scaled by
                // 0.25 — so the pyramid carried 1.25x the game's energy and
                // the composite's add blew out the highlights.
                float4 s = tex2D(_MainTex, i.uv + float2(-tx.x, -tx.y));
                s += tex2D(_MainTex, i.uv + float2(tx.x, -tx.y));
                s += tex2D(_MainTex, i.uv + float2(-tx.x, tx.y));
                s += tex2D(_MainTex, i.uv + float2(tx.x, tx.y));
                s *= 0.25;
                // the game's downsample (@3302): out = max(avg - threshold, 0)
                // * intensity — the intensity folds HERE, once, and the
                // threshold is the negative offset (audience doc Q3: the
                // threshold applies at EVERY pyramid level, per TAP, and
                // there is NO intensity multiply at the composite).
                return max(s - _Parameter.z, 0.0) * _Parameter.w;
            }
            ENDCG
        }

        // pass 2: HORIZONTAL blur — the game runs H before V
        // (CreateBloomTexture blit pass 2 = BlurHorizontal feeds the pass-3
        // blit at insn 395/442, fastbloom decode). 9 taps on x, per-tap
        // threshold fold, no intensity.
        Pass
        {
            Name "BLOOMBLURH"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_blur_h
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Parameter;   // (blur*2^-9/aspect, blur*2^-9, threshold, intensity)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            static const float TAPS[5] = {0.0, 1.0, 2.0, 3.0, 5.0};
            static const float WEIGHTS[5] = {0.225, 0.15, 0.11, 0.075, 0.0525};

            float4 frag_blur_h(v2f i) : SV_Target
            {
                float2 step_h = float2(_Parameter.x, 0.0);
                // the game's HORIZONTAL blur (fastbloom sp_10, pass 3
                // 'BlurHorizontal'): it accumulates the taps with the fixed
                // weights, does NOT fold the threshold, and multiplies the
                // accumulated result by the intensity. the VERTICAL blur
                // (sp_08, pass 2 'BlurVertical') is the one that folds.
                // the 9-tap kernel samples the CENTER once (weight 0.225)
                // plus each offset once per side; the weights sum to
                // exactly 1.0.
                float4 sum = tex2D(_MainTex, i.uv) * WEIGHTS[0];
                [unroll]
                for (int s = 1; s < 5; s++)
                {
                    float4 t0 = tex2D(_MainTex, i.uv + step_h * TAPS[s]);
                    float4 t1 = tex2D(_MainTex, i.uv - step_h * TAPS[s]);
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum * _Parameter.w;
            }
            ENDCG
        }

        // pass 3: VERTICAL blur — same taps on y, no threshold fold (the H
        // pass folded it), no intensity (the downsample folded it).
        Pass
        {
            Name "BLOOMBLURV" 
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_blur_v
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Parameter;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            static const float TAPS[5] = {0.0, 1.0, 2.0, 3.0, 5.0};
            static const float WEIGHTS[5] = {0.225, 0.15, 0.11, 0.075, 0.0525};

            float4 frag_blur_v(v2f i) : SV_Target
            {
                float2 step_v = float2(0.0, _Parameter.y);
                // the game's VERTICAL blur (fastbloom sp_08, pass 2
                // 'BlurVertical'): per tap `max(sample - threshold, 0)`,
                // fixed weights, and NO intensity multiply. the HORIZONTAL
                // blur (sp_10, pass 3) is the one that does not fold and
                // does multiply the intensity.
                float4 sum = max(tex2D(_MainTex, i.uv) - _Parameter.z, 0.0) * WEIGHTS[0];
                [unroll]
                for (int s = 1; s < 5; s++)
                {
                    float4 t0 = tex2D(_MainTex, i.uv + step_v * TAPS[s]);
                    float4 t1 = tex2D(_MainTex, i.uv - step_v * TAPS[s]);
                    t0 = max(t0 - _Parameter.z, 0.0);
                    t1 = max(t1 - _Parameter.z, 0.0);
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum;
            }
            ENDCG
        }
    }
    Fallback Off
}
