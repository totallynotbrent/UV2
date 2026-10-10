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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_composite
            #pragma multi_compile _ DIFFUSION_ON
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            sampler2D _DiffusionTex;
            float _BloomBlendMode;   // authored BloomBlendMode: 1 = additive
            float _bloomDofWeight;   // the game publishes id 179 pre-composite
            float _BloomIsScreenBlend;  // id 225: 0 for the Add family
            float4 _DiffusionParams;  // (lift, -, -, fp)
            float4 _DiffusionScreen;  // (-, -, -, lever: add vs screen)
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
                // the game's film-variant composite (postbloom_composite_
                // pass0_math.md §4 @51362 + audience doc Q3): C = t2(rgb
                // source) * cb0[139].y (filmPower) + bloom - ADDITIVE, not
                // screen. the screen form lifts every midtone; the additive
                // form is why the official concert's stage falls to near
                // black in cutaways (filmPower 0) and stays dim through mid
                // scenes. the fork ran the real game shader with
                // OutputDimmer 1 and its look matched.
                float4 col = saturate(src) * _BloomShaping + bloom;

#ifdef DIFFUSION_ON
                // the game's diffusion composite (PostDiffusionBloom_Rich
                // pass-0 PS @54922, uv2_diffusion_composite_decoded.md):
                //   A = src + diff*fp
                //   B = src + diff*fp*(1 - lever*src)   (add vs screen lever)
                //   C = B*B*(2-bloom) + bloom + lift
                //   out = max(B, max(C, 0))
                // the square compresses the diffused energy, the (2-bloom)
                // screen term adds bloom, the lift term keeps a faint floor
                // (the game's barely-visible background in dark scenes), and
                // the max against B never lets the result fall under the
                // linear base. the old invented form was a threshold
                // bright-pass that over-brightened lit frames.
                // fp/lever/lift are serialized material defaults in the game
                // (rows 139.y/163.w/151.x - not runtime-published); the
                // worksheet's diffusion keys carry no direct counterpart so
                // they stay uniforms with decode-derived defaults.
                float4 diff = tex2D(_DiffusionTex, i.uv);
                float3 shaped = saturate(src).rgb * _BloomShaping;
                float3 B = shaped + diff.rgb * _DiffusionParams.w * (1.0 - _DiffusionScreen.w * shaped);
                float3 C = B * B * (2.0 - bloom.rgb) + bloom.rgb + _DiffusionParams.x;
                col.rgb = max(B, max(C, 0.0));
#endif
                return col;
            }
            ENDCG
        }

        // pass 1: the 4-tap downsample — 2x2 neighborhood, threshold fold
        // once, intensity once (the only fold the downsample does).
        Pass
        {
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
                float4 s = tex2D(_MainTex, i.uv);
                s += tex2D(_MainTex, i.uv + float2(tx.x, tx.y));
                s += tex2D(_MainTex, i.uv + float2(-tx.x, tx.y));
                s += tex2D(_MainTex, i.uv + float2(tx.x, -tx.y));
                s += tex2D(_MainTex, i.uv + float2(-tx.x, -tx.y));
                s *= 0.25;
                // the game's downsample (@3302): max(avg + _Parameter.x, 0)
                // * scale - the threshold folds HERE as the negative offset and
                // the intensity multiplies HERE (audience_props_bloom_answers
                // Q3: 'threshold as -threshold offset applies at EVERY pyramid
                // level, per TAP, BEFORE the blur weights. there is NO separate
                // intensity multiply at the composite').
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
                // the game's 9-tap kernel samples the CENTER once (weight
                // 0.225) plus each offset once per side; the weights sum to
                // exactly 1.0. folding the center into the (t0+t1) pair
                // doubled it and left the pyramid at 1.225 total gain - the
                // over-bright bloom. the threshold fold applies to EVERY
                // tap including the center (fastbloom decode: per-tap
                // max(sample + offset, 0) before the weights).
                float4 sum = max(tex2D(_MainTex, i.uv) - _Parameter.z, 0.0) * WEIGHTS[0];
                [unroll]
                for (int s = 1; s < 5; s++)
                {
                    float4 t0 = tex2D(_MainTex, i.uv + step_h * TAPS[s]);
                    float4 t1 = tex2D(_MainTex, i.uv - step_h * TAPS[s]);
                    t0 = max(t0 - _Parameter.z, 0.0);
                    t1 = max(t1 - _Parameter.z, 0.0);
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum;
            }
            ENDCG
        }

        // pass 3: VERTICAL blur — same taps on y, no threshold fold (the H
        // pass folded it), no intensity (the downsample folded it).
        Pass
        {
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
                // same center-once kernel as the H pass; weights sum to 1.0.
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
                // the blur passes carry no intensity scale: the game folds
                // intensity at the downsample only (audience doc Q3); the
                // threshold folds per tap at every pyramid level (@5346).
                return sum;
            }
            ENDCG
        }
    }
    Fallback Off
}
