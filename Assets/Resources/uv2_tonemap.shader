// the game's output stage.
//
// gallop's pipeline has no tonemap class of its own: its seventy
// post-pipeline classes carry tonecurve and exposure passes, but both are
// driven by the song's authored timeline keys, and those tracks are empty
// for every surveyed song. gallop.renderpipeline.finalblitpass::execute
// then finishes with a plain commandbuffer::blit, which copies the buffer
// with no shader at all.
//
// the picture still needs shaping, and unity's own universal render
// pipeline supplies it: the game ships urp 14 on unity 2022.3.62f2, the
// same generations this project uses, so its Neutral curve is the one the
// official frames took. measured against the official landmarks it beats
// a straight passthrough on every frame tested.
//
// so this pass carries two things:
//
//   the authored grade. gallop's tone curve pass (section 7 of
//   uv2_six_postfx_families_decoded.md) is a 256x2 monochrome lut: row 0
//   the tone curve, row 1 the mask curve, graded with
//
//       out = lerp(min_level, max_level, curve(sample))
//
//   where the mask row covers the pixels at or above the mask levels and
//   the depth mask chooses which level pair applies.
//
//   the transfer to display gamma. the concert is a gamma-space project:
//   the game's own playersettings carry m_activecolorspace: 0, and this
//   project matches it, so unity writes the frame back as it stands.
//   the game's final blit carries no material, so nothing encodes the
//   frame on the way out, and the values in the buffer are already
//   display-referred. encoding them a second time lifts the whole picture
//   into a veil, so the transfer stays off unless a bench asks for it
//   (UV2_TONEMAP_ENCODE=srgb).
//
// order: the grade runs on the scene's display-referred values, the way its
// correction levels imply, and the transfer follows it.

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
            #pragma multi_compile __ TONEMAP_ENCODE_SRGB
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

            // unity's sRGB transfer function: the exact piecewise curve, not
            // the 2.2 power approximation. linear values below 0.0031308
            // take the linear branch, the rest the power branch.
            float3 linear_to_srgb(float3 c)
            {
                c = max(c, 0.0);
                float3 hi = 1.055 * pow(max(c, 1e-6), 1.0 / 2.4) - 0.055;
                float3 lo = c * 12.92;
                float3 use_lo = step(c, 0.0031308);
                return hi * (1.0 - use_lo) + lo * use_lo;
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

            #if TONEMAP_ENCODE_SRGB
                // the frame is presented in gamma space, so the linear buffer
                // takes unity's sRGB transfer on the way out. the curve
                // clamps first: the transfer's power branch is undefined
                // above one, and the backbuffer would take that as white.
                graded = linear_to_srgb(clamp(graded, 0.0, 1.0));
            #else
                graded = clamp(graded, 0.0, 1.0);
            #endif

                return fixed4(graded, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
