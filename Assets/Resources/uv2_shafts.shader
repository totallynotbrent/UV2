// screen-space light shafts: the game's IndirectLightShafts pass
// (four-texture weighted sun shafts, six-families doc §27) approximated
// with the decoded SunShaftsComposite math (§760): radial distance from
// the screen pivot, 1/r falloff, intensity x density, additive tint.
Shader "live/uv2_shafts"
{
    Properties
    {
        _MainTex ("source", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            // pass 0: bright-pass + first radial step toward the pivot
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _ShaftsParam; // x intensity, y density, z falloff floor, w unused
            float2 _ShaftsPivot; // screen-space uv pivot
            float _ShaftsThreshold;
            float4 _ShaftsTintA;
            float4 _ShaftsTintB;
            float _ShaftsBlend;  // cb0[143] blend between the two color groups
            float _ShaftsAspect; // width / height
            float4 _MainTex_TexelSize;

            float3 bright_sample(float2 uv)
            {
                float3 c = tex2D(_MainTex, uv).rgb;
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                return c * saturate(lum - _ShaftsThreshold) / max(lum, 1e-4);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                // radial step: sample toward the pivot, accumulate.
                float2 delta = (_ShaftsPivot - i.uv) * _ShaftsParam.y;
                float3 acc = 0;
                acc += bright_sample(i.uv);
                acc += bright_sample(i.uv + delta * 0.25);
                acc += bright_sample(i.uv + delta * 0.5);
                acc += bright_sample(i.uv + delta * 0.75);
                acc += bright_sample(i.uv + delta);
                return float4(acc * 0.2, 1);
            }
            ENDCG
        }
        Pass
        {
            // pass 1: composite — 1/r falloff, tint blend, additive
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _ShaftsTex;
            float4 _ShaftsParam; // x intensity, y density, z falloff floor, w blend
            float2 _ShaftsPivot;
            float _ShaftsAspect;
            float4 _ShaftsTintA;
            float4 _ShaftsTintB;
            float _ShaftsBlend;

            float4 frag(v2f_img i) : SV_Target
            {
                float3 base = tex2D(_MainTex, i.uv).rgb;
                // radial distance from the pivot, aspect-corrected.
                float2 d = (i.uv - _ShaftsPivot) * float2(_ShaftsAspect, 1);
                float r = max(length(d), _ShaftsParam.z);
                float shaftmask = 1.0 / r;
                // tint blend between the two color groups (cb0[143] blend).
                float3 tint = lerp(_ShaftsTintA.rgb, _ShaftsTintB.rgb, _ShaftsBlend);
                float3 shafts = tex2D(_ShaftsTex, i.uv).rgb * shaftmask * _ShaftsParam.x;
                shafts *= tint;
                return float4(base + shafts, 1);
            }
            ENDCG
        }
    }
}
