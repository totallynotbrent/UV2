// the game's ToneCurvePass (uv2_six_postfx_families_decoded.md section 7)
// and its final output pass.
//
// the game does NOT ship a filmic tone map. it grades the concert with an
// authored per-key curve baked into a 256x2 monochrome lut: row 0 the tone
// curve, row 1 the mask curve. the grade is
//
//     out = lerp(min_level, max_level, curve(sample))
//
// with the mask row covering the pixels above the mask levels and a depth
// mask choosing which level pair applies. the keys that drive it
// (ToneCurveKeys) are disabled in every surveyed song, so the authored pass
// contributes nothing and the chain falls through to the output pass, which
// is effectively a passthrough (the game's own output shader reduces to
// abs(scene) with the target's gamma encoding).
//
// this shader therefore offers:
//   TONEMAP_GAME_CURVE  the authored lut grade, when a tone curve key is on
//   default             the passthrough the game lands on without one
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
            #if TONEMAP_GAME_CURVE
                // the curve lut is 256 wide and 2 tall: row 0 holds the tone
                // curve, row 1 the mask curve. the game samples the tone row
                // at v=0.25 and the mask row at v=0.75.
                // the game grades each channel on its own: the red channel
                // is looked up at u=scene.r, the green at u=scene.g and the
                // blue at u=scene.b, all from the same lut row.
                float3 tone = float3(
                    tex2D(_CurveTex, float2(scene.r, 0.25)).r,
                    tex2D(_CurveTex, float2(scene.g, 0.25)).r,
                    tex2D(_CurveTex, float2(scene.b, 0.25)).r);
                float3 mask_value = float3(
                    tex2D(_CurveTex, float2(scene.r, 0.75)).r,
                    tex2D(_CurveTex, float2(scene.g, 0.75)).r,
                    tex2D(_CurveTex, float2(scene.b, 0.75)).r);
                float3 graded = lerp(_MinLevel.rgb, _MaxLevel.rgb, tone);
                float3 mask_graded = lerp(_MaskMinLevel.rgb, _MaskMaxLevel.rgb, mask_value);
                float3 out_rgb = lerp(graded, mask_graded, step(_MaskMinLevel.rgb, scene));
                return fixed4(clamp(out_rgb, 0.0, 1.0), 1.0);
            #else
                // no authored tone curve: the game hands the frame to its
                // output pass, which preserves the scene values.
                return fixed4(clamp(scene, 0.0, 1.0), 1.0);
            #endif
            }
            ENDCG
        }
    }
    Fallback Off
}
