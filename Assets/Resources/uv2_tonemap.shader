// the game's output stage: unity's universal render pipeline tonemaps the
// final image in its final blit, and gallop's ToneCurvePass grades the
// concert with an authored per-key curve.
//
// both halves are here:
//
//   the tonemap is unity's Neutral curve, lifted verbatim from the render
//   pipeline package (com.unity.render-pipelines.core 14.0.12,
//   ShaderLibrary/Color.hlsl). the game runs the same package generation
//   (unity 2022.3.62f2, urp 14), so this is the curve that shapes the
//   official frames. it also supplies the highlight roll-off the picture
//   needs: the scene renders well above 1.0, and without it the stage
//   lights flood the frame to solid white.
//
//   the tone curve is gallop's authored pass (uv2_six_postfx_families_decoded.md
//   section 7): a 256x2 monochrome lut - row 0 the tone curve, row 1 the
//   mask curve - graded with
//
//       out = lerp(min_level, max_level, curve(sample))
//
//   with the mask row covering the pixels at or above the mask levels and
//   the depth mask choosing which level pair applies. the keys that drive
//   it (ToneCurveKeys) are disabled in every surveyed song, so this path is
//   off in practice and the chain runs the tonemap alone.
//
// order: the scene is tonemapped first, then the authored grade, which
// works on display-referred values the way its correction levels imply.

Shader "live/uv2_tonemap"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
        _Exposure ("Exposure", Float) = 1
        _CurveTex ("Curve Lut", 2D) = "gray" {}
        _MinLevel ("Min Level", Color) = (0,0,0,1)
        _MaxLevel ("Max Level", Color) = (1,1,1,1)
        _MaskMinLevel ("Mask Min Level", Color) = (0,0,0,1)
        _MaskMaxLevel ("Mask Max Level", Color) = (1,1,1,1)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ TONEMAP_GAME_CURVE
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Exposure;
            sampler2D _CurveTex;
            float4 _MinLevel;
            float4 _MaxLevel;
            float4 _MaskMinLevel;
            float4 _MaskMaxLevel;

            // unity's Neutral tonemap (hable/hejl/frostbite fit), copied from
            // com.unity.render-pipelines.core 14.0.12 ShaderLibrary/Color.hlsl
            // so the shaping matches the render pipeline the game ships with.
            float3 neutral_curve(float3 x, float a, float b, float c, float d, float e, float f)
            {
                return ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f;
            }

            float3 neutral_tonemap(float3 x)
            {
                x = max(0.0.xxx, x);

                const float a = 0.2;
                const float b = 0.29;
                const float c = 0.24;
                const float d = 0.272;
                const float e = 0.02;
                const float f = 0.3;
                const float white_level = 5.3;
                const float white_clip = 1.0;

                float3 white_scale = (1.0).xxx / neutral_curve(white_level, a, b, c, d, e, f);
                x = neutral_curve(x * white_scale, a, b, c, d, e, f);
                x *= white_scale;
                x /= white_clip.xxx;
                return x;
            }

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

            fixed4 frag(v2f i) : SV_Target
            {
                float3 scene = tex2D(_MainTex, i.uv).rgb * _Exposure;
                float3 graded = neutral_tonemap(scene);
            #if TONEMAP_GAME_CURVE
                // the curve lut is 256 wide and 2 tall: row 0 holds the tone
                // curve, row 1 the mask curve. the game looks up each channel
                // on its own at u=that channel's value.
                float3 tone = float3(
                    tex2D(_CurveTex, float2(graded.r, 0.25)).r,
                    tex2D(_CurveTex, float2(graded.g, 0.25)).r,
                    tex2D(_CurveTex, float2(graded.b, 0.25)).r);
                float3 mask_value = float3(
                    tex2D(_CurveTex, float2(graded.r, 0.75)).r,
                    tex2D(_CurveTex, float2(graded.g, 0.75)).r,
                    tex2D(_CurveTex, float2(graded.b, 0.75)).r);
                float3 tone_graded = lerp(_MinLevel.rgb, _MaxLevel.rgb, tone);
                float3 mask_graded = lerp(_MaskMinLevel.rgb, _MaskMaxLevel.rgb, mask_value);
                graded = lerp(tone_graded, mask_graded, step(_MaskMinLevel.rgb, graded));
            #endif
                return fixed4(clamp(graded, 0.0, 1.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
