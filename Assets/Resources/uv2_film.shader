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
        _PostFilmColor0 ("Color0", Color) = (0,0,0,0)
        _PostFilmColor1 ("Color1", Color) = (0,0,0,0)
        _PostFilmColor2 ("Color2", Color) = (0,0,0,0)
        _PostFilmColor3 ("Color3", Color) = (0,0,0,0)
        _DepthClip ("DepthClip", Float) = 0
        _DepthClipZone ("DepthClipZone", Float) = 0
        _DepthPower ("DepthPower", Float) = 0
        _PostFilmIsInverseVignette ("IsInverseVignette", Float) = 0
        _PostFilmIsWithoutDepth ("IsWithoutDepth", Float) = 0
        _PostFilmPower ("Power", Float) = 1
        _PostFilmOffsetParam ("OffsetParam", Vector) = (0,0,0,0)
        _PostFilmOptionParam ("OptionParam", Vector) = (0,0,0,0)
        _PostFilmRollParameter ("RollParameter", Vector) = (0,0,0,0)
        _PostFilmScaleParameter ("ScaleParameter", Vector) = (1,1,1,1)
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
            float _PostFilmIsInverseVignette;
            float _PostFilmIsWithoutDepth;
            float _DepthClipGate;
            float _DepthClipZone;
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

            // the game's mask (film variant ps_4_0 @90722, register-exact):
            //   p = ((uv*aspect - 0.5*aspect).xy * scale.xy), roll-rotated,
            //       recentered (+0.5, x/aspect), + _PostFilmOffsetParam, -0.5;
            //   d2 = dot(p, p) * optionParam.x
            //   num = (optionParam.y > 0) ? d2 : 1e-4
            //   m   = num / (d2 + optionParam.y * (1 - d2))
            // y == 0 -> m = 1e-4/(d2+y) ~ tiny (near-black film); y > 0 -> vignette.
            // the inverse flag tail (@90722 ops 41/46-48, with depthTerm=1 at
            // DepthClip>=1.5 - every 1004 key): mask = inv*(-2*dt*pre) + dt*pre
            // = -pre_mask. the game's inverse layers SUBTRACT the vignette at
            // the frame edges; they never paint a center disc. all 177
            // painting keys on 1004 carry the inverse flag, so our old 1-m
            // flip added a hot saturated disc where the game darkens.
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
                float soft = _PostFilmOptionParam.y;
                // the exact decoded rational (@76838 preamble):
                // (soft>0 ? (0.0001 - d2) : d2) / (d2 + soft - soft*d2).
                float num = soft > 0.0 ? (0.0001 - d2) : d2;
                float den = d2 + soft - soft * d2;
                float m = den != 0.0 ? num / den : 0.5;
                return m;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                float v = mask(i.uv);
                // the decoded preamble (@76838): depthTerm from the scrolled
                // source tap gated by _DepthClip (152.y, remap clip>1 -> 0),
                // the 152.z gate, then the IsWithoutDepth kill (164.x), then
                // the inverse lerp: mask = lerp(v*dt, (1-dt)*dt - v*dt,
                // IsInverseVignette). with the game's DepthClip=2 the tap
                // term is inert and depthTerm = 1 - IsWithoutDepth.
                float dt = 1.0 - v * _DepthClipGate;
                dt = ((1.0 - dt >= _DepthClipZone) ? (1.0 - dt) : 0.0) + dt;
                dt *= (1.0 - _PostFilmIsWithoutDepth);
                float mask_full = lerp(v * dt, (1.0 - dt) * dt - v * dt,
                                       _PostFilmIsInverseVignette);
                // the ps scales every mode's layer color by cb0[152].x =
                // _DepthPower = key.depthPower (uv2_film_mode_bodies_decoded.md
                // §5) - a valid layer with depthPower==0 paints exactly zero.
                // _PostFilmPower (key.filmPower) is the validity term.
                float p = _DepthPower;
                // the layer gradient: bilinear ramp of c0..c3 over the
                // warp coords (mx, my), the game's G. the flat-c0 port
                // darkened whole regions where the game ramps to white.
                float2 gw = i.uv - 0.5;
                float mx = saturate(gw.x + 0.5), my = saturate(gw.y + 0.5);
                fixed4 g0 = lerp(_PostFilmColor0, _PostFilmColor1, mx);
                fixed4 g1 = lerp(_PostFilmColor2, _PostFilmColor3, mx);
                fixed4 c0 = lerp(g0, g1, my);

            #ifdef MODE_NONE
                return scene;
            #elif MODE_LERP
                // o = lerp(C, L, a) with L = G scaled by the power row.
                return lerp(scene, c0 * saturate(p), saturate(p));
            #elif MODE_ADD
                // L = G*dt*152.x*G.a; o = C + L.
                return scene + c0 * (p * dt) * c0.a;
            #elif MODE_MUL
                // o = C + 152.x*(C*L - C) = C*(1 + p*(G-1)) with dt folded.
                return scene + p * dt * (scene * c0 - scene);
            #elif MODE_VIGNETTE_LERP
                // o = C + (mask*152.x)*(G - C).
                return scene + (mask_full * p) * (c0 - scene);
            #elif MODE_VIGNETTE_ADD
                // L = (mask*152.x)*G; o = C + L.
                return scene + (mask_full * p) * c0;
            #elif MODE_VIGNETTE_MUL
                // o = C + 152.x*mask*(C*G - C) (@104130).
                return scene + p * mask_full * (scene * c0 - scene);
            #elif MODE_MONOCHROME
                // o = lerp(C, lerp(C, lum*G.rgb, G.a), c0.a) with the lum
                // third-average scaled by 152.x (no vignette preamble).
                float g = dot(scene.rgb, float3(0.3333, 0.3333, 0.3333)) * p;
                fixed4 mg = fixed4(lerp(scene.rgb, g.xxx * c0.rgb, c0.a), 1);
                return lerp(scene, mg, c0.a);
            #elif MODE_SCREENBLEND
                // o = screen(C, G*mask-ish) *152.x fold: the decoded shape
                // scales the blend result by 152.x then adds the base.
                fixed4 sb = 1 - (1 - scene) * (1 - saturate(c0 * p));
                return lerp(scene, sb, p);
            #elif MODE_VIGNETT_SCREENBLEND
                fixed4 sb = 1 - (1 - scene) * (1 - saturate(c0 * (mask_full * p)));
                return lerp(scene, sb, mask_full * p);
            #else
                return scene;
            #endif
            }
            ENDCG
        }
    }
    Fallback Off
}
