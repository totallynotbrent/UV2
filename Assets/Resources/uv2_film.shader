// the film overlay shader: the game's PostBloom_Rich overlay reduced to the
// pure-color layer path (no movie textures in the live concert path).
// the mask algebra is register-decoded from the film variant ps_4_0
// (postbloom listing @181622): aspect-correct, scale, roll, recenter,
// offset, d2 = dist2 * optionParam.x, mask m = d2/(d2 + opt.y*(1-d2)).
Shader "live/uv2_film"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ MODE_NONE MODE_LERP MODE_ADD MODE_MUL MODE_VIGNETTE_LERP MODE_VIGNETTE_ADD MODE_VIGNETTE_MUL MODE_MONOCHROME MODE_SCREENBLEND MODE_VIGNETT_SCREENBLEND
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
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

            // the game's mask (film variant ps_4_0 @181622, register-exact):
            //   p = ((uv*aspect - 0.5*aspect).xy * scale.xy), roll-rotated,
            //       recentered (+0.5, x/aspect), + _PostFilmOffsetParam;
            //   d2 = dot((p-0.5)*2, (p-0.5)*2) * optionParam.x
            //   num = (optionParam.y <= 0) ? 1e-4 : d2      [GE 0,y -> z=1 when y<=0]
            //   m   = num / (num + optionParam.y * (1 - num))
            // y == 0 -> m = 1 everywhere (full-screen film); y > 0 -> vignette.
            float mask(float2 uv)
            {
                float aspect = _MainTex_TexelSize.w > 0 ? _MainTex_TexelSize.z / _MainTex_TexelSize.w : 1.0;
                float2 p = uv * float2(aspect, 1.0) - float2(0.5 * aspect, 0.5);
                p *= _PostFilmScaleParameter.xy;
                float s = _PostFilmRollParameter.x;
                float c = _PostFilmRollParameter.y;
                float2 r = float2(p.x * c - p.y * s, p.x * s + p.y * c);
                r += float2(0.5 * aspect, 0.5);
                r.x /= aspect;
                r += _PostFilmOffsetParam.xy;
                float2 d = (r - float2(0.5, 0.5)) * 2.0;
                float d2 = dot(d, d) * _PostFilmOptionParam.x;
                float b = _PostFilmOptionParam.y;
                float num = b <= 0.0 ? 1e-4 : d2;
                return num / (num + b * (1.0 - num));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                float v = mask(i.uv);
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
