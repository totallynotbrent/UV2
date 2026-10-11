// the chromatic fringe + lens distortion warp, one shader two passes.
// pass 0 chromatic: RGB channels sampled at uv + channel offset * amount
// (the fork's SetChromaticAberration(clamp01(power*0.05)) consumer; offsets
// from the key's red/green/blueOffset Vector2s, clip from the key's clip).
// pass 1 lens distortion: barrel/pincushion warp around the authored center
// (the fork's GallopLensDistortionPass: r2 = dot(uv-center, uv-center),
// uv += (uv-center) * r2 * intensity, scale compensation).
Shader "live/uv2_chromatic_lens"
{
    Properties
    {
        _MainTex ("Base", 2D) = "" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // pass 0: chromatic aberration fringe.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_chroma
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _ChromaAmount;
            float _ChromaClip;
            float4 _ChromaR;   // xy = red offset
            float4 _ChromaG;   // xy = green offset
            float4 _ChromaB;   // xy = blue offset

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

            float4 frag_chroma(v2f i) : SV_Target
            {
                // clamp clamps to (0, _ChromaClip); authored 1004 keys carry
                // clip 0, which would pin every sample to (0,0) = one texel
                // smeared over the frame. use (0, 1) as the floor so the
                // authored clamp only kills offsets >1 and clip==0 passes uv
                // through untouched.
                float2 off_r = i.uv + _ChromaR.xy * _ChromaAmount;
                float2 off_g = i.uv + _ChromaG.xy * _ChromaAmount;
                float2 off_b = i.uv + _ChromaB.xy * _ChromaAmount;
                float clip_hi = _ChromaClip > 0.0 ? _ChromaClip : 1.0;
                off_r = clamp(off_r, 0.0, clip_hi);
                off_g = clamp(off_g, 0.0, clip_hi);
                off_b = clamp(off_b, 0.0, clip_hi);
                float4 src = tex2D(_MainTex, i.uv);
                float r = tex2D(_MainTex, off_r).r;
                float g = tex2D(_MainTex, off_g).g;
                float b = tex2D(_MainTex, off_b).b;
                return float4(r, g, b, src.a);
            }
            ENDCG
        }

        // pass 1: lens distortion warp.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_lens
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _LensIntensity;
            float2 _LensCenter;
            float _LensScale;

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

            float4 frag_lens(v2f i) : SV_Target
            {
                float2 d = i.uv - _LensCenter;
                float r2 = dot(d, d);
                float2 warped = i.uv + d * r2 * _LensIntensity;
                // scale compensation toward the center so the frame fills
                // the target like the fork's Scale field.
                warped = _LensCenter + (warped - _LensCenter) * _LensScale;
                return tex2D(_MainTex, warped);
            }
            ENDCG
        }
    }
    Fallback Off
}
