// the audience pen lights: the song's pattern sheet holds a lattice of ten
// light sprites across and thirty-two down, and the game's crowd meshes
// carry no uv channel at all. the port gives every light its own sprite
// cell and phase, then the sprite climbs the sheet's rows at the tempo the
// song's choreography asks for. each light adds its own colour to the hall,
// and the song's authored bloom threshold gives it the halo the official
// has.
Shader "live/uv2_cyalume"
{
    Properties
    {
        _MainTex ("pattern", 2D) = "white" {}
        _Frame ("frame", Float) = 0
        _Tint ("tint", Color) = (1,1,1,1)
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

            sampler2D _MainTex;
            float _Frame;
            float4 _Tint;

            static const float SHEET_COLUMNS = 10.0;
            static const float SHEET_ROWS = 32.0;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 cell : TEXCOORD1;
                float2 phase : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 cell : TEXCOORD1;
                float2 phase : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.cell = v.cell;
                o.phase = v.phase;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // the light walks up the sheet's rows, each on its own
                // phase, so the crowd shimmers instead of pulsing together.
                float row = i.cell.y + floor(_Frame + i.phase.x * SHEET_ROWS);
                row = fmod(row, SHEET_ROWS);

                float u = (i.cell.x + i.uv.x) / SHEET_COLUMNS;
                float v = (row + i.uv.y) / SHEET_ROWS;

                fixed4 sprite = tex2D(_MainTex, float2(u, v));
                return fixed4(sprite.rgb * sprite.a * _Tint.rgb, 1.0);
            }
            ENDCG
        }
    }
}
