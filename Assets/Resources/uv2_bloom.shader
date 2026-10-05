// the game's FastBloom pyramid (out/GAME_BLOOM_PIPELINE_DECODED.md +
// out/audience_props_bloom_answers.md). the decode's contract:
//   blit A (pass 1, downsample): _Parameter = (1/w, 1/h, threshold, intensity)
//     ps: max(sample + _Parameter.z, 0) * _Parameter.w  — threshold folds as a
//     negative-offset clamp here and intensity enters exactly once, here.
//   blit B (pass 1, neutral):    _Parameter = (1/w, 1/h, 0, 1)
//   blits C/D (passes 2+3, blur): _Parameter = (blur*2^-9/aspect, blur*2^-9,
//     threshold, intensity) — the threshold fold repeats per tap at every
//     level so bright energy never accumulates unscaled.
//   composite: soft-add, no intensity multiply — r1 = bloom*gate + 1 shaping.
Shader "live/uv2_bloom"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // pass 0: the threshold downsample — 4-tap average, threshold folded
        // per tap, intensity applied once (blit A).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_down
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _Parameter;   // (1/w, 1/h, threshold, intensity)

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
                s += tex2D(_MainTex, i.uv - tx);
                s *= 0.25;
                // the decode: max(sample + threshold, 0) * intensity, per tap.
                return max(s + _Parameter.z, 0.0) * _Parameter.w;
            }
            ENDCG
        }

        // pass 1: vertical blur — 9-tap fixed weights, threshold fold per tap.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_blur_v
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

            static const float WEIGHTS[5] = {0.15, 0.225, 0.11, 0.075, 0.0525};

            float4 frag_blur_v(v2f i) : SV_Target
            {
                float2 step_v = float2(0.0, _Parameter.y);
                float4 sum = 0;
                [unroll]
                for (int s = 0; s < 5; s++)
                {
                    float4 t0 = max(tex2D(_MainTex, i.uv + step_v * (s * 0.5)) + _Parameter.z, 0.0);
                    float4 t1 = max(tex2D(_MainTex, i.uv - step_v * (s * 0.5)) + _Parameter.z, 0.0);
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum * _Parameter.w;
            }
            ENDCG
        }

        // pass 2: horizontal blur — same 9-tap on x.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_blur_h
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

            static const float WEIGHTS[5] = {0.15, 0.225, 0.11, 0.075, 0.0525};

            float4 frag_blur_h(v2f i) : SV_Target
            {
                float2 step_h = float2(_Parameter.x, 0.0);
                float4 sum = 0;
                [unroll]
                for (int s = 0; s < 5; s++)
                {
                    float4 t0 = max(tex2D(_MainTex, i.uv + step_h * (s * 0.5)) + _Parameter.z, 0.0);
                    float4 t1 = max(tex2D(_MainTex, i.uv - step_h * (s * 0.5)) + _Parameter.z, 0.0);
                    sum += (t0 + t1) * WEIGHTS[s];
                }
                return sum * _Parameter.w;
            }
            ENDCG
        }

        // pass 3: the soft-add composite — source + bloom, no intensity
        // multiply (the game's r1 = bloom*gate + 1 screen-blend shaping).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_composite
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            float _BloomGate;   // _BloomIsScreenBlend: 0 add, 1 screen blend

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
                // screen blend: 1 - (1-src)*(1-bloom) = src + bloom - src*bloom;
                // plain add otherwise. never a composite-time intensity scale.
                float4 screen = 1.0 - (1.0 - src) * (1.0 - saturate(bloom));
                return lerp(src + saturate(bloom), screen, _BloomGate);
            }
            ENDCG
        }
    }
    Fallback Off
}
