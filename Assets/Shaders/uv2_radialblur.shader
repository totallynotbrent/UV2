// the game's Cygames/ImageEffects/RadialBlur port. five blur variants
// (radial, radial+depth, x-only, y-only, ellipse-rotated) plus the composite,
// selected by blit pass index exactly like the game's 2*(type-1) dispatch.
// decode + register evidence: ~/umadump/out/uv2_radialblur_decoded.md
Shader "live/uv2_radialblur"
{
    Properties
    {
        _MainTex ("", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _BlurTex;
        sampler2D _CameraDepthTexture;
        float4 _ZBufferParams;

        // _BlurParam (game id 212): offset.xy, ellipseDir.xy.
        float4 _BlurParam;
        // _BlurParamEx (game id 213): step, startArea, areaDiff, depthFront.
        float4 _BlurParamEx;
        // game id 216: normalized depthBack.
        float _BlurEndArea;
        // game id 190: cancel rect (min.xy, max.xy); id 191: blend length.
        float4 _DepthCancelRect;
        float _DepthCancelBlendLength;
        // jitter seed (game _GlobalScreenUVScrollParam row).
        float2 _GlobalScreenUVScrollParam;
        // per-axis step scale (game UnityPerMaterial row 0).
        float2 _RadialBlurAxisScale;
        // mip bias the game's sample_b carries on the downsampled blur.
        float _RadialBlurSampleBias;

        // the game's tap weights: center .19, k=1..9 descending .17..01.
        static const float W[9] =
        {
            0.17, 0.15, 0.13, 0.11, 0.09, 0.07, 0.05, 0.03, 0.01
        };

        // linearize the raw depth buffer exactly like the game's
        // 1/(cb0[23].x*z + cb0[23].y) chain (_ZBufferParams form).
        float linear_depth(float2 uv)
        {
            float z = tex2D(_CameraDepthTexture, uv).r;
            return 1.0 / (_ZBufferParams.x * z + _ZBufferParams.y);
        }

        // the soft depth-cancel rect factor: inside = 1, fading to 0 across
        // blendLength outside. the game's div_sat edge pair + >=3.5 test.
        float rect_factor(float2 uv)
        {
            float2 lo = _DepthCancelRect.xy;
            float2 hi = _DepthCancelRect.zw;
            float2 dmin = uv - lo;
            float2 dmax = hi - uv;
            float inside = step(0.0, dmin.x) + step(0.0, dmin.y)
                         + step(0.0, dmax.x) + step(0.0, dmax.y);
            if (inside >= 3.5)
            {
                return 1.0;
            }
            float bl = max(1e-5, _DepthCancelBlendLength);
            float f = saturate(min(dmin.x, min(dmin.y, min(dmax.x, dmax.y))) / bl);
            return f;
        }

        // the front/back double-gate: full blur in [front, back], clean
        // outside, lerp across when front > back (the game's mad chain).
        float depth_gate(float2 uv)
        {
            float d = linear_depth(uv);
            float front = _BlurParamEx.w;
            float back = _BlurEndArea;
            float behind_front = step(d, front);
            float before_back = step(d, back);
            float back_gt_front = step(front, back);
            // front<=back: d<=front -> before_back*1 else gate; the game's
            // composed factor collapses to inside-mask * before_back.
            float inside = behind_front * before_back;
            float edge = back_gt_front * inside
                       + (1.0 - back_gt_front) * before_back;
            return edge;
        }

        // one tap mask: depth gate x rect factor (the game multiplies both
        // into every tap's weight; gates off publish identity constants).
        float tap_mask(float2 uv, bool depth_on, bool rect_on)
        {
            float m = 1.0;
            if (depth_on) m *= depth_gate(uv);
            if (rect_on) m *= rect_factor(uv);
            return m;
        }

        // weighted 10-tap smear: center + k=1..9 along dir. when masks
        // engage the tap folds back to center (the game's difference form
        // is algebraically this with center re-added).
        float4 smear(float2 uv, float2 dir_step, bool depth_on, bool rect_on)
        {
            float2 jittered = frac(uv + _GlobalScreenUVScrollParam);
            float4 acc = tex2Dbias(_MainTex, float4(uv, 0.0, _RadialBlurSampleBias)) * 0.19;
            [unroll]
            for (int k = 1; k <= 9; k++)
            {
                float2 tuv = uv + dir_step * float(k);
                if (depth_on || rect_on) tuv = frac(tuv + _GlobalScreenUVScrollParam);
                float m = tap_mask(tuv, depth_on, rect_on);
                float4 tap_c = tex2Dbias(_MainTex, float4(uv, 0.0, _RadialBlurSampleBias));
                float4 tap_k = tex2Dbias(_MainTex, float4(tuv, 0.0, _RadialBlurSampleBias));
                acc += W[k - 1] * lerp(tap_c, tap_k, m);
            }
            return saturate(acc);
        }

        // dir math per type (the five decoded variants).
        // 1/2: normalize(uv - offset) * axisScale * step
        // 3: x-only, 4: y-only (single axis, sign preserved)
        // 5: ellipse dir rotated by roll, per-axis scale from _BlurParam.zw
        float2 blur_dir(float2 uv, int type)
        {
            float2 c = _BlurParam.xy;
            float step_ = _BlurParamEx.x;
            if (type == 3)
            {
                float dx = uv.x - c.x;
                float inv = rsqrt(max(1e-8, dx * dx));
                return float2(dx * inv * _RadialBlurAxisScale.x * step_, 0.0);
            }
            if (type == 4)
            {
                float dy = uv.y - c.y;
                float inv = rsqrt(max(1e-8, dy * dy));
                return float2(0.0, dy * inv * _RadialBlurAxisScale.y * step_);
            }
            if (type == 5)
            {
                float2 d = uv - c;
                float inv = rsqrt(max(1e-8, dot(d, d)));
                return d * inv * _BlurParam.zw * step_;
            }
            float2 d = uv - c;
            float inv = rsqrt(max(1e-8, dot(d, d)));
            return d * inv * _RadialBlurAxisScale * step_;
        }

        float4 blur_ps(float2 uv, int type, bool depth_on, bool rect_on)
        {
            float2 dir_step = blur_dir(uv, type);
            // the game's double-sided 19-tap form runs when gates engage;
            // the basic 10-tap otherwise.
            if (!depth_on && !rect_on)
            {
                return smear(uv, dir_step, false, false);
            }
            float2 c = _BlurParam.xy;
            float2 jittered = frac(uv + _GlobalScreenUVScrollParam);
            float4 center = tex2Dbias(_MainTex, float4(jittered, 0.0, _RadialBlurSampleBias));
            float4 acc_pos = center * (0.17 + 0.19);
            float4 acc_neg = center * (0.17 + 0.19);
            [unroll]
            for (int k = 1; k <= 9; k++)
            {
                float m_p = tap_mask(uv + dir_step * float(k), depth_on, rect_on);
                float m_n = tap_mask(uv - dir_step * float(k), depth_on, rect_on);
                float4 tp = tex2Dbias(_MainTex, float4(uv + dir_step * float(k), 0.0, _RadialBlurSampleBias));
                float4 tn = tex2Dbias(_MainTex, float4(uv - dir_step * float(k), 0.0, _RadialBlurSampleBias));
                acc_pos += W[k - 1] * m_p * (tp - center);
                acc_neg += W[k - 1] * m_n * (tn - center);
            }
            return saturate(acc_pos + acc_neg);
        }

        // the composite: o0 = main + f * (blur - main), f the area/depth
        // factor. exactly the game's sp_08/sp_10 shape.
        float4 composite_ps(float2 uv, int type, bool depth_on, bool rect_on)
        {
            float4 main_c = tex2D(_MainTex, uv);
            float4 blur_c = tex2D(_BlurTex, uv);
            float2 c = _BlurParam.xy;
            float dist;
            if (type == 3) dist = abs(uv.x - c.x);
            else if (type == 4) dist = abs(uv.y - c.y);
            else dist = distance(uv, c);
            float f = saturate((dist - _BlurParamEx.y) / max(1e-5, _BlurParamEx.z));
            if (depth_on) f *= depth_gate(uv);
            if (rect_on) f *= rect_factor(uv);
            return main_c + f * (blur_c - main_c);
        }
        ENDCG

        // pass 0: type 1 radial blur (game pass = 2*(type-1)).
        Pass { Name "RadialBlur1"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return blur_ps(i.uv, 1, false, false); }
            ENDCG
        }
        // pass 1: composite for type 1.
        Pass { Name "RadialBlur1Comp"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return composite_ps(i.uv, 1, false, false); }
            ENDCG
        }
        // pass 2/3: type 2 radial + depth + rect gates.
        Pass { Name "RadialBlur2"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return blur_ps(i.uv, 2, true, true); }
            ENDCG
        }
        Pass { Name "RadialBlur2Comp"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return composite_ps(i.uv, 2, true, true); }
            ENDCG
        }
        // pass 4/5: type 3 x-only.
        Pass { Name "RadialBlur3"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return blur_ps(i.uv, 3, false, false); }
            ENDCG
        }
        Pass { Name "RadialBlur3Comp"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return composite_ps(i.uv, 3, false, false); }
            ENDCG
        }
        // pass 6/7: type 4 y-only.
        Pass { Name "RadialBlur4"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return blur_ps(i.uv, 4, false, false); }
            ENDCG
        }
        Pass { Name "RadialBlur4Comp"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return composite_ps(i.uv, 4, false, false); }
            ENDCG
        }
        // pass 8/9: type 5 ellipse-rotated.
        Pass { Name "RadialBlur5"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return blur_ps(i.uv, 5, false, false); }
            ENDCG
        }
        Pass { Name "RadialBlur5Comp"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            { return composite_ps(i.uv, 5, false, false); }
            ENDCG
        }
    }
    Fallback Off
}
