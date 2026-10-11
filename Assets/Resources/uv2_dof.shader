// the depth-of-field chain: coc prefilter (signed, background-only per the
// game's unorm saturation), separable blur with the coc riding alpha, and
// the far-blend composite. the math follows the decoded PostDofBloom_Rich
// shape: coc = (1/linearZ - focal) * scale.
Shader "live/uv2_dof"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // pass 0: the coc prefilter — color passthrough, coc into alpha.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_coc
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _DofCurveParams;   // (1, 1, farBlend, offsetY)
            float4 _DofFocusParams;   // (focal01, scale, foreground, smoothness)
            float4 _DofFocalMeters;   // (focal_m, farClip, 0, 0)
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

            float4 frag_coc(v2f i) : SV_Target
            {
                float4 scene = tex2D(_MainTex, i.uv);
                float rawZ = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                // the game's coc: eye meters (1/(a*z+b) = linear eye depth)
                // minus the authored focal meters, signed so only the
                // beyond-focus side blurs (unorm saturates the near side).
                float eye_m = LinearEyeDepth(rawZ);
                float coc = (eye_m - _DofFocalMeters.x) * _MainTex_TexelSize.y * 8.0 * _DofFocusParams.y;
                // CalculateMaxCoc's resolution cap: coc = min(coc, (coc/50*4+6)/height).
                coc = min(coc, (coc / 50.0 * 4.0 + 6.0) * _MainTex_TexelSize.y);
                scene.a = saturate(coc);
                return scene;
            }
            ENDCG
        }

        // pass 1: horizontal blur, max-coc of the taps.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_blur
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _DofBlurSpread;

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

            float4 frag_blur(v2f i) : SV_Target
            {
                // the game's downsample: 9 taps (center + 4 steps x 2 sides),
                // 1/7 weight (deliberately 1.286x hot, saturated), the coc
                // rides the CENTER tap only — never mixed across taps.
                float4 c = tex2D(_MainTex, i.uv);
                float coc = c.a;
                float r = coc * _DofBlurSpread * _MainTex_TexelSize.x * 8.0;
                float4 sum = c;
                [unroll]
                for (int s = 1; s <= 4; s++)
                {
                    float off = r * s * 0.25;
                    float4 s1 = tex2D(_MainTex, i.uv + float2(off, 0));
                    float4 s2 = tex2D(_MainTex, i.uv - float2(off, 0));
                    sum += s1 + s2;
                }
                sum.rgb = saturate(sum.rgb * (1.0 / 7.0));
                sum.a = coc;
                return sum;
            }
            ENDCG
        }

        // pass 2: vertical blur (same shape).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_blur
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _DofBlurSpread;

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

            float4 frag_blur(v2f i) : SV_Target
            {
                // the game's downsample: 9 taps (center + 4 steps x 2 sides),
                // 1/7 weight (deliberately 1.286x hot, saturated), the coc
                // rides the CENTER tap only — never mixed across taps.
                float4 c = tex2D(_MainTex, i.uv);
                float coc = c.a;
                float r = coc * _DofBlurSpread * _MainTex_TexelSize.y * 8.0;
                float4 sum = c;
                [unroll]
                for (int s = 1; s <= 4; s++)
                {
                    float off = r * s * 0.25;
                    float4 s1 = tex2D(_MainTex, i.uv + float2(0, off));
                    float4 s2 = tex2D(_MainTex, i.uv - float2(0, off));
                    sum += s1 + s2;
                }
                sum.rgb = saturate(sum.rgb * (1.0 / 7.0));
                sum.a = coc;
                return sum;
            }
            ENDCG
        }

        // pass 3: composite — lerp scene to blurred by the far blend.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_comp
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _DofBlurred;
            float4 _DofCurveParams;

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

            float4 frag_comp(v2f i) : SV_Target
            {
                float4 scene = tex2D(_MainTex, i.uv);
                float4 blurred = tex2D(_DofBlurred, i.uv);
                float f = saturate(blurred.a * _DofCurveParams.z);
                return float4(lerp(scene.rgb, blurred.rgb, f), scene.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
