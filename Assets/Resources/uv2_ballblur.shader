// the game's ball-blur family (PASS_BALL_BLUR_EXTRACTION/_SPREAD/_ADD,
// dof_pipeline_decoded.md): bright-pass extraction at the threshold, radial
// spread scaled by the power factor, additive recombine - the ball glow halo.
Shader "live/uv2_ballblur"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // pass 0: extraction + spread + add in one composite: sample a ring
        // of taps around the pixel, keep taps above the brightness
        // threshold, weight them by distance falloff, add back scaled by
        // the intensity.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_ball
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _BallBlurParams;   // (powerFactor, brightnessThreshold, brightnessIntensity, spread)
            float4 _MainTex_TexelSize;

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

            float4 frag_ball(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float power = max(_BallBlurParams.x, 0.0);
                if (power <= 0.0001 || _BallBlurParams.z <= 0.0001)
                    return src;

                // radial spread: 8 taps on a ring at spread texels, keeping
                // the bright band above the threshold.
                float2 tx = _MainTex_TexelSize.xy * _BallBlurParams.w;
                float4 halo = 0;
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * 0.7853981634;   // 2*pi/8
                    float2 dir = float2(cos(ang), sin(ang));
                    float3 tap = tex2D(_MainTex, i.uv + dir * tx).rgb;
                    float lum = dot(tap, float3(0.299, 0.587, 0.114));
                    halo.rgb += tap * saturate(lum - _BallBlurParams.y);
                }
                halo.rgb *= (power / 8.0) * _BallBlurParams.z;
                return saturate(float4(src.rgb + halo.rgb, src.a));
            }
            ENDCG
        }
    }
    Fallback Off
}
