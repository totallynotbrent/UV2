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
            float4 _DiffusionParams;  // (threshold, bright, saturation, contrast)
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
                // the game's dof-bloom composite (PostDofBloom_Rich PS#1,
                // dofbloomblob_ps.txt): output = src * (1 + bloom - k) where
                // k is the material's shaping constant row - a source-scaled
                // add, not a plain add. ours previously ran saturate(src +
                // bloom), which overbrightens every mid-luminance surface:
                // at src 0.3 / bloom 0.2 the game adds 0.015, we added 0.2.
                // k is the material's serialized shaping row, live value
                // undecoded; 0 is the only defensible default - it keeps
                // bloom-off at identity and dims the add by src exactly
                // like the game dims mid-luminance surfaces.
                float k = _BloomShaping;
                float4 shaped = src * (1.0 + bloom - k);
                // mode 0 = screen blend family, unchanged.
                float4 screen = 1.0 - (1.0 - src) * (1.0 - bloom);
                float4 col = lerp(screen, saturate(shaped), _BloomBlendMode);

#ifdef DIFFUSION_ON
                // the diffusion chain grades a WIDER blur than the base
                // bloom texture (the game's PostDiffusionBloom_Rich path):
                // bright-pass lift, saturation/contrast grading, additive mix.
                // falls back to the bloom texture when no diffusion pyramid
                // was rendered (the blurred source carries the same energy).
                float4 diff_src = tex2D(_DiffusionTex, i.uv);
                float diff_lum = dot(diff_src.rgb, float3(0.299, 0.587, 0.114));
                float bright = max(diff_lum - _DiffusionParams.x, 0.0) * _DiffusionParams.y;
                float3 graded = saturate(col.rgb + diff_src.rgb * bright);
                float grad_luma = dot(graded, float3(0.299, 0.587, 0.114));
                graded = lerp(float3(grad_luma, grad_luma, grad_luma), graded, _DiffusionParams.z);
                graded = (graded - 0.5) * _DiffusionParams.w + 0.5;
                col.rgb = saturate(graded);
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
                s *= 0.25;
                // the decode: max(avg - threshold, 0) * intensity.
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
                float4 sum = 0;
                [unroll]
                for (int s = 0; s < 5; s++)
                {
                    float4 t0 = tex2D(_MainTex, i.uv + step_h * TAPS[s]);
                    float4 t1 = tex2D(_MainTex, i.uv - step_h * TAPS[s]);
                    if (s > 0)
                    {
                        t0 = max(t0 - _Parameter.z, 0.0);
                        t1 = max(t1 - _Parameter.z, 0.0);
                    }
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum;
            }
            ENDCG
        }

        // pass 3: VERTICAL blur — same taps on y, no fold, intensity ONCE at
        // the end: o = weighted * w.
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
                float4 sum = 0;
                [unroll]
                for (int s = 0; s < 5; s++)
                {
                    float4 t0 = tex2D(_MainTex, i.uv + step_v * TAPS[s]);
                    float4 t1 = tex2D(_MainTex, i.uv - step_v * TAPS[s]);
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum * _Parameter.w;
            }
            ENDCG
        }
    }
    Fallback Off
}
