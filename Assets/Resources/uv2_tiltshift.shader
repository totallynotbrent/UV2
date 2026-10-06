// the game's TiltShiftHdrLensBlur, ported register-exact from the decoded
// DXBC blob (uv2_tiltshift_decoded.md): 7 passes sharing one vertex stage;
// pass selection = quality*2 + (mode!=1); planar/radial COC by mode; blur
// radius in pixels scaled by the source texel size; 28-tap jittered disc
// (icb table verbatim); /29 single-dir and /57 dual-dir normalization; pass 6
// is a pure copy through the flip-corrected uv.
Shader "live/uv2_tiltshift"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _BlurSize;      // maxBlurSize, px
        float _BlurArea;      // blurArea
        float4 _Params;        // (offset.x, offset.y, sin(roll), cos(roll))

        // the game's icb: 28 jittered-disc taps, 4-decimal, outer-weighted.
        static const float2 disc[28] =
        {
            float2( 0.624630,  0.543370), float2(-0.134140, -0.944880),
            float2( 0.387720, -0.434750), float2( 0.121260, -0.192820),
            float2(-0.203880,  0.111330), float2( 0.831140, -0.292180),
            float2( 0.107590, -0.578390), float2( 0.282850,  0.790360),
            float2(-0.366220,  0.395160), float2( 0.755910,  0.219160),
            float2(-0.526100,  0.023860), float2(-0.882160, -0.244710),
            float2(-0.488880, -0.293300), float2( 0.440140, -0.085580),
            float2( 0.211790,  0.513730), float2( 0.054830,  0.957010),
            float2(-0.590010, -0.705090), float2(-0.800650,  0.246310),
            float2(-0.194240, -0.184020), float2(-0.436670,  0.767510),
            float2( 0.216660,  0.116020), float2( 0.156960, -0.856000),
            float2(-0.758210,  0.583630), float2( 0.992840, -0.029040),
            float2(-0.222340, -0.579070), float2( 0.550520, -0.669840),
            float2( 0.464310,  0.281150), float2(-0.072140,  0.605540),
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;     // raw uv (PS#1..#6)
            float2 uv_flip : TEXCOORD1; // flip-corrected uv (PS#7)
        };

        // the shared VS: standard blit transform, plus the game's
        // _MainTex_TexelSize.y < 0 flip test for the copy-pass uv.
        v2f vert_tilt(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord.xy;
            float flip = _MainTex_TexelSize.y < 0.0 ? 1.0 : 0.0;
            o.uv_flip = float2(v.texcoord.x, flip ? 1.0 - v.texcoord.y : v.texcoord.y);
            return o;
        }

        // the offset point in [-1,1]^2 space (PS#1..#6).
        #define TILT_D(uv) ((uv) * 2.0 + _Params.xy - 1.0)
        // the signed distance to the focal line (dir = (sin roll, cos roll)).
        #define TILT_SDIST(d) dot(_Params.zw, d)

        float4 tap_lod(float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 1)); }
        float4 tap(float2 uv) { return tex2D(_MainTex, uv); }

        // the planar blur radius in pixels.
        float planar_blur_px(float2 uv)
        {
            float sdist = TILT_SDIST(TILT_D(uv));
            return min(abs(sdist) * _BlurArea, _BlurSize);
        }

        // the radial blur radius in pixels.
        float radial_blur_px(float2 uv)
        {
            float2 d = TILT_D(uv);
            return clamp(dot(d, d) * _BlurArea, 0.0, _BlurSize);
        }

        // the 29-tap single-direction loop: center + 28 disc taps.
        float4 blur_single(float2 uv, float blur_px)
        {
            float2 texel = _MainTex_TexelSize.xy;
            float4 c = tap(uv);
            float3 acc = c.rgb;
            for (int i = 0; i < 28; i++)
                acc += tap_lod(uv + disc[i] * blur_px * texel).rgb;
            return float4(acc / 29.0, c.a);
        }

        // the 57-tap planar q2 kernel: center + 28 (center + one-sided offset)
        // pairs, so the center pixel carries 29/57 of the weight.
        float4 blur_dual_planar(float2 uv, float blur_px)
        {
            float2 texel = _MainTex_TexelSize.xy;
            float4 c = tap(uv);
            float3 acc = c.rgb;
            for (int i = 0; i < 28; i++)
            {
                float2 off = disc[i] * blur_px * texel;
                acc += tap_lod(uv).rgb;
                acc += tap_lod(uv - off).rgb;
            }
            return float4(acc / 57.0, c.a);
        }

        // the 57-tap radial q2 kernel: fixed +-2-texel symmetric disc,
        // radius independent of all params (PS#6's quirk, kept for fidelity).
        float4 blur_dual_radial(float2 uv, float blur_px)
        {
            float2 texel = _MainTex_TexelSize.xy;
            float4 c = tap(uv);
            float3 acc = c.rgb;
            for (int i = 0; i < 28; i++)
            {
                acc += tap_lod(uv + disc[i] * 2.0 * texel).rgb;
                acc += tap_lod(uv - disc[i] * 2.0 * texel).rgb;
            }
            return float4(acc / 57.0, c.a);
        }
        ENDCG

        // pass 0: planar COC mask, |sdist * area| * 0.5.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_coc_planar
            #pragma target 3.0
            float4 frag_coc_planar(v2f i) : SV_Target
            {
                float sdist = TILT_SDIST(TILT_D(i.uv));
                float m = abs(sdist * _BlurArea) * 0.5;
                return float4(m, m, m, m);
            }
            ENDCG
        }

        // pass 1: radial COC mask, dot(d,d) * area * 0.5.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_coc_radial
            #pragma target 3.0
            float4 frag_coc_radial(v2f i) : SV_Target
            {
                float2 d = TILT_D(i.uv);
                float m = dot(d, d) * _BlurArea * 0.5;
                return float4(m, m, m, m);
            }
            ENDCG
        }

        // pass 2: planar q1 blur (29-tap).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_planar_q1
            #pragma target 3.0
            float4 frag_planar_q1(v2f i) : SV_Target
            {
                float blur_px = planar_blur_px(i.uv);
                if (blur_px < 0.01) return tap(i.uv);
                return blur_single(i.uv, blur_px);
            }
            ENDCG
        }

        // pass 3: radial q1 blur (29-tap).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_radial_q1
            #pragma target 3.0
            float4 frag_radial_q1(v2f i) : SV_Target
            {
                float blur_px = radial_blur_px(i.uv);
                if (blur_px < 0.01) return tap(i.uv);
                return blur_single(i.uv, blur_px);
            }
            ENDCG
        }

        // pass 4: planar q2 blur (57-tap one-sided pairs).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_planar_q2
            #pragma target 3.0
            float4 frag_planar_q2(v2f i) : SV_Target
            {
                float blur_px = planar_blur_px(i.uv);
                if (blur_px < 0.01) return tap(i.uv);
                return blur_dual_planar(i.uv, blur_px);
            }
            ENDCG
        }

        // pass 5: radial q2 blur (fixed +-2-texel symmetric disc).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_radial_q2
            #pragma target 3.0
            float4 frag_radial_q2(v2f i) : SV_Target
            {
                float blur_px = radial_blur_px(i.uv);
                if (blur_px < 0.01) return tap(i.uv);
                return blur_dual_radial(i.uv, blur_px);
            }
            ENDCG
        }

        // pass 6: pure copy through the flip-corrected uv.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_tilt
            #pragma fragment frag_copy
            #pragma target 3.0
            float4 frag_copy(v2f i) : SV_Target
            {
                return tap(i.uv_flip);
            }
            ENDCG
        }
    }
    Fallback Off
}
