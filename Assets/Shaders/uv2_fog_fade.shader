// the screen-space fog + fade shader: the classic frustum-ray GlobalFog math
// (the game's pass reuses it) with the authored fade quad on top.
Shader "live/uv2_fog_fade"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
        _FogColor ("Fog Color", Color) = (1,1,1,1)
        _FadeColor ("Fade Color", Color) = (0,0,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_fog
            #pragma multi_compile _ FOG_HEIGHT_ON
            #pragma multi_compile _ FADE_ON
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _HeightParams;
            float4 _DistanceParams;
            float4 _SceneFogParams;
            float4 _SceneFogMode;
            float4 _FrustumCornersWS;
            float4 _CameraWS;
            float4 _FogColor;
            float4 _FadeColor;
            float _HeightDensity;
            float _FogStart;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 ray : TEXCOORD1;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                int idx = (int)(o.uv.x * 2 + o.uv.y * 3);
                o.ray = _FrustumCornersWS[idx];
                return o;
            }

            fixed4 frag_fog(v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                float fog_factor = 0;

            #ifdef FOG_HEIGHT_ON
                // the game's GlobalFog pixel math, register-exact per the
                // d3dasm decode (@1422 height pass / @3258 simple pass):
                //   eye = 1 / (zBufferParams.x*depth + zBufferParams.y)
                //   d_base = (radial ? |eyeRay| : eye*far) + _DistanceParams.x
                //   height pass adds: d -= radS * hcorr where
                //     hAbove = eye*ray.y - _HeightParams.x
                //     k = 1 - 2*_HeightParams.z
                //     hgrad = (min(hAbove*k, 0))^2 / |eye*ray.y + 1e-5|
                //     hcorr = _HeightParams.z*hAbove - hgrad
                //     radS = |eyeRay * _HeightParams.w|
                //   f = mode 1: saturate(d*SP.z + SP.w)
                //       mode 2: 1-exp(-d*SP.y)
                //       mode 3: 1-exp(-(d*SP.x)^2)
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float eye = LinearEyeDepth(rawDepth);
                float3 ray = i.ray.xyz;
                bool is_sky = rawDepth >= 0.999999;

                float3 er = eye * ray;
                float d;
                if (_SceneFogMode.y > 0.5)
                    d = length(er);
                else
                    d = eye * _ProjectionParams.z;

                float hcorr = 0;
                float radS = 0;
                // the height correction (@1422): worldY = eye*ray.y + camY;
                // hAbove = worldY - HP.x; k = 1-2*HP.z; the plane term adds
                // HP.y back (hcorr uses worldY - HP.x + HP.y verbatim);
                // slope = |eye*ray.y + 1e-5|; hgrad = min(hAbove*k,0)^2/slope.
                float worldY = eye * ray.y + _CameraWS.y;
                float hAbove = worldY - _HeightParams.x;
                float k = 1.0 - 2.0 * _HeightParams.z;
                float t0 = min(hAbove * k, 0.0);
                float slope = abs(eye * ray.y) + 1e-5;
                float hgrad = (t0 * t0) / slope;
                hcorr = _HeightParams.z * (hAbove + _HeightParams.y) - hgrad;
                radS = length(er * _HeightParams.w);
                d = d - radS * hcorr;

                d = max(d + _DistanceParams.x - _FogStart, 0.0);

                if (_SceneFogMode.x < 1.5)
                    fog_factor = saturate(d * _SceneFogParams.z + _SceneFogParams.w);
                else if (_SceneFogMode.x < 2.5)
                    fog_factor = 1.0 - exp(-d * _SceneFogParams.y);
                else
                {
                    float a = d * _SceneFogParams.x;
                    fog_factor = 1.0 - exp(-(a * a));
                }
                if (is_sky) fog_factor = 1.0;
            #endif

                fixed4 col = scene;
            #ifdef FOG_HEIGHT_ON
                col.rgb = lerp(scene.rgb, _FogColor.rgb, saturate(fog_factor));
            #endif
            #ifdef FADE_ON
                col.rgb = lerp(col.rgb, _FadeColor.rgb, _FadeColor.a);
            #endif
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
