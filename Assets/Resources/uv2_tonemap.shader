// the final tone map for the concert chain. the game renders the concert
// into an hdr buffer and compresses it once at the end of the post chain;
// an ldr buffer clips the additive film layers and the bloom to white.
// this shader is the aces fitted curve (stephen hill's fit of the academy
// rrt+odt), the same shape unity's aces tonemapping mode applies.
Shader "live/uv2_tonemap"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ TONEMAP_NEUTRAL
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Exposure;

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

            float3 rrt(float3 v)
            {
                float3 a = v * (v + 0.0245786) - 0.000090537;
                float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
                return a / b;
            }

            // unity's neutral tone mapper (the khronos pbr neutral fit unity
            // ships as its "Neutral" mode): it keeps the blacks black and
            // the hues intact, unlike the aces fit which lifts and
            // desaturates the shadows.
            float3 neutral(float3 color)
            {
                float start_compression = 0.8 - 0.04;
                float desaturation = 0.15;
                float3 x = min(color, start_compression.xxx);
                // the quadratic knee below the compression point, the flat
                // offset above it. hlsl has no vector-valued ternary, so the
                // select is a step.
                float3 below = 1.0 - step(start_compression.xxx, x);
                float3 offset = lerp(0.04.xxx, 0.08 * x * x, below);
                color -= offset;
                float peak = max(color.r, max(color.g, color.b));
                if (peak < start_compression) return color;
                float d = 1.0 - start_compression;
                float new_peak = 1.0 - d * d / (peak + d - start_compression);
                color *= new_peak / peak;
                float g = 1.0 - 1.0 / (desaturation * (peak - new_peak) + 1.0);
                return lerp(color, new_peak.xxx, g);
            }

            float3 aces_fit(float3 c)
            {
                // the academy's input and output matrices.
                c = float3(
                    (c.r * 0.59719 + c.g * 0.35458 + c.b * 0.04823),
                    (c.r * 0.07600 + c.g * 0.90834 + c.b * 0.01566),
                    (c.r * 0.02840 + c.g * 0.13383 + c.b * 0.83777));
                c = rrt(c);
                c = float3(
                    (c.r * 1.60475 + c.g * -0.53108 + c.b * -0.07367),
                    (c.r * -0.10208 + c.g * 1.10813 + c.b * -0.00605),
                    (c.r * -0.00327 + c.g * -0.07276 + c.b * 1.07602));
                return clamp(c, 0.0, 1.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb * _Exposure;
            #if TONEMAP_NEUTRAL
                return fixed4(clamp(neutral(c), 0.0, 1.0), 1.0);
            #else
                return fixed4(aces_fit(c), 1.0);
            #endif
            }
            ENDCG
        }
    }
    Fallback Off
}
