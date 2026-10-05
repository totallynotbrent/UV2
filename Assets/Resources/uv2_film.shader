// the film overlay shader: the game's PostBloom_Rich overlay passes reduced to
// the pure-color layer path (no movie textures in the live concert path).
// mode keywords match the game's keyword-selection array exactly.
Shader "live/uv2_film"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // pass 0/1: the standard film pass + its inverse-vignette twin.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ MODE_NONE MODE_LERP MODE_ADD MODE_MUL MODE_VIGNETTE_LERP MODE_VIGNETTE_ADD MODE_VIGNETTE_MUL MODE_MONOCHROME MODE_SCREENBLEND MODE_VIGNETT_SCREENBLEND
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _PostFilmPower;
            float _DepthPower;
            float _DepthClip;
            float4 _PostFilmOffsetParam;
            float4 _PostFilmOptionParam;
            float4 _PostFilmColor0;
            float4 _PostFilmColor1;
            float4 _PostFilmColor2;
            float4 _PostFilmColor3;
            float4 _PostFilmRollParameter;
            float4 _PostFilmScaleParameter;

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

            // the vignette mask: distance from the layer's center, scaled by
            // the authored offset/roll/scale, 0 at center -> 1 at the corners.
            float vignette(float2 uv)
            {
                float2 p = uv - 0.5;
                float sinr = _PostFilmRollParameter.x;
                float cosr = _PostFilmRollParameter.y;
                p = float2(p.x * cosr - p.y * sinr, p.x * sinr + p.y * cosr);
                p *= _PostFilmScaleParameter.xy;
                p -= _PostFilmOffsetParam.xy;
                return saturate(length(p) * 2.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                float v = vignette(i.uv);
                float p = _PostFilmPower;
                fixed4 c0 = _PostFilmColor0;

            #ifdef MODE_NONE
                return scene;
            #elif MODE_LERP
                return lerp(scene, c0, saturate(p));
            #elif MODE_ADD
                return scene + c0 * p;
            #elif MODE_MUL
                return scene * lerp(float4(1,1,1,1), c0, saturate(p));
            #elif MODE_VIGNETTE_LERP
                return lerp(scene, c0, saturate(p * v));
            #elif MODE_VIGNETTE_ADD
                return scene + c0 * (p * v);
            #elif MODE_VIGNETTE_MUL
                return scene * lerp(float4(1,1,1,1), c0, saturate(p * v));
            #elif MODE_MONOCHROME
                float g = dot(scene.rgb, float3(0.299, 0.587, 0.114));
                return lerp(scene, c0 * fixed4(g, g, g, 1), saturate(p));
            #elif MODE_SCREENBLEND
                return 1 - (1 - scene) * (1 - c0 * p);
            #elif MODE_VIGNETT_SCREENBLEND
                return 1 - (1 - scene) * (1 - c0 * (p * v));
            #else
                return scene;
            #endif
            }
            ENDCG
        }
    }
    Fallback Off
}
