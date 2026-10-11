// the color-correction grade: samples the director's 256x1 rgb LUT with the
// scene's luma, then applies the authored saturation (the game's
// ColorCorrectionPass evaluates its curves into this LUT each frame; the
// shader side is a straight channel remap by scene value).
Shader "live/uv2_colorgrade"
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Lut;
            float _Saturation;

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
                fixed4 scene = tex2D(_MainTex, i.uv);
                // per-channel remap through the graded curve: the LUT row is
                // (r, g, b) built from each channel's own curve, so each channel
                // looks up its own value (lut.x for red, .y for green, .z for blue).
                float r = tex2D(_Lut, float2(scene.r, 0.5)).r;
                float g = tex2D(_Lut, float2(scene.g, 0.5)).g;
                float b = tex2D(_Lut, float2(scene.b, 0.5)).b;
                float3 graded = float3(r, g, b);
                // the authored saturation: lerp toward luma like the game's
                // final PS pass (mad luma, 1-sat, sat*color).
                float luma = dot(graded, float3(0.2125, 0.7154, 0.0721));
                graded = lerp(float3(luma, luma, luma), graded, saturate(_Saturation));
                return fixed4(graded, scene.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
