// the floor mirror: the game draws the stage reflection through a render
// pipeline feature (Cygames/MirrorAndShadow), so the floor material alone
// never shows it. this shader draws the reflection the mirror camera already
// renders, distorted by the stage's own distortion map and limited by the
// stage's own mask, at the rate the song's environment track publishes.
Shader "live/uv2_mirror"
{
    Properties
    {
        _ReflectionTex ("reflection", 2D) = "black" {}
        _DistMap ("distortion map", 2D) = "gray" {}
        _MaskTex ("mask", 2D) = "white" {}
        _ReflectionRate ("reflection rate", Float) = 0
        _Color ("tint", Color) = (1,1,1,1)
        _DistMap_ST ("distortion tiling", Vector) = (1,1,0,0)
        _DistPower ("distortion power", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float3 world : TEXCOORD2;
            };

            sampler2D _ReflectionTex;
            sampler2D _DistMap;
            sampler2D _MaskTex;
            float _ReflectionRate;
            float4 _Color;
            float4 _DistMap_ST;
            float4 _DistPower;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _DistMap);
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // the reflection camera renders the mirrored scene into a
                // screen aligned target, so the floor samples it in screen space.
                float2 suv = i.screen.xy / i.screen.w;

                // the distortion map carries the ripple direction in rg and
                // its height in b; the power scales how far the sample moves.
                float4 dist = tex2D(_DistMap, i.uv);
                float amp = _DistPower.x;
                if (amp <= 0.0) amp = 1.0;
                float2 offset = (dist.rg - 0.5) * 2.0 * amp;
                // the ripples are laid out across the stage, not across the
                // screen, so the distortion follows the floor's own axes.
                float2 tilt = normalize(float2(abs(_DistPower.y) + 0.0001, abs(_DistPower.z) + 0.0001));
                float2 delta = float2(offset.x * tilt.y - offset.y * tilt.x, offset.x * tilt.x + offset.y * tilt.y);

                float3 reflection = tex2D(_ReflectionTex, saturate(suv + delta)).rgb;

                // the mask decides where the floor is wet enough to mirror,
                // and the rate says how strongly the reflection comes
                // through.
                float mask = tex2D(_MaskTex, i.uv).r;
                float rate = saturate(_ReflectionRate);

                float3 col = reflection * mask * rate * _Color.rgb;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
