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
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                float interpDepth = Linear01Depth(rawDepth);
                float3 vdir = i.ray.xyz;
                float dist = length(vdir) * interpDepth;

                float2 dist_params = _DistanceParams.xy;
                float scene_fog_z = 1;
                float fog_start = 0;

                // exp/exp2: 1-exp(-d*density), linear: (d-start)/(end-start)
                if (_SceneFogMode.x > 1.5)
                {
                    float d = max(0, dist - fog_start) * dist_params.x;
                    if (_SceneFogMode.x > 2.5)
                        fog_factor = 1 - exp(-exp2(d));
                    else
                        fog_factor = 1 - exp(-d);
                }
                else
                {
                    fog_factor = saturate((dist - _SceneFogParams.z) * _SceneFogParams.w);
                }
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
