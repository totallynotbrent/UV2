// the film overlay shader: the game's PostBloom_Rich overlay reduced to the
// pure-color layer path (no movie textures in the live concert path).
// every formula below is register-exact against the game's COLOR_ONLY
// + MODE_* pixel programs of Gallop/ImageEffects/Rich/PostBloom_Rich
// (postbloom_rich.decompressed, keyword-indexed per subprogram).
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
        _PostFilmColorType ("ColorType", Float) = 0
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
            float _DepthClipZone;
            float4 _PostFilmScaleParameter;
            float _PostFilmColorType;

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

            // the game's mask preamble, register-exact:
            //   p   = roll/scale-warp(v1)     cb0[161] roll sin/cos/aspect,
            //                                   cb0[162] scale
            //   uv  = p + cb0[153].xy - 0.5    the offset applies BEFORE the
            //                                   0.5 subtraction
            //   d2  = dot(uv,uv) * cb0[154].x  optionParam.x = radius
            //   soft= optionParam.y
            //   r0.x = (soft>0 ? (0.0001 - d2) : d2)
            //          / (d2 + (soft - soft*d2))
            // the rational vignette is NEGATIVE inside the radius and falls
            // to -inf outward when soft>0, so the plain layer paints the
            // frame edges faintly and the inverse layer tops out near 0.25.
            // the old port saturated the distance and divided by a positive
            // denominator, which made the inverse variant fill the whole
            // centre at full strength.
            float mask(float2 uv)
            {
                float aspect = _PostFilmRollParameter.z > 0 ? _PostFilmRollParameter.z : 1.0;
                float2 p = uv * float2(aspect, 1.0) - float2(0.5 * aspect, 0.5);
                p *= _PostFilmScaleParameter.xy;
                float s = _PostFilmRollParameter.x;
                float c = _PostFilmRollParameter.y;
                float2 r = float2(p.x * c - p.y * s, p.x * s + p.y * c);
                r += float2(0.5 * aspect, 0.5);
                r.x /= aspect;
                r -= float2(0.5, 0.5);
                r += _PostFilmOffsetParam.xy;
                float2 d2v = r;
                float d2 = dot(d2v, d2v) * _PostFilmOptionParam.x;
                float soft = _PostFilmOptionParam.y;
                float num = soft > 0.0 ? (0.0001 - d2) : d2;
                float den = d2 + (soft - soft * d2);
                // the denominator vanishes at the radius edge, so the game
                // divides only where it is safe and lets the edge fall out
                // of the layer. a division by a vanishing denominator
                // blows up to infinity, which saturates the layer to the
                // full colour instead of letting it taper.
                if (den > 1e-5) return num / den;
                return 0.0;
            }

            // the game's depth chain (the cb0[152]/[164] block):
            //   dtRaw = 1 - movie.r * cb0[152].y      (cb0[152].y = _DepthClip)
            //   gate  = (1 - dtRaw) >= cb0[152].z ? 0 : 1
            //   dt    = gate * (1 - dtRaw) + dtRaw
            //   dt   *= (1 - cb0[164].x)             (cb0[164].x = IsWithoutDepth)
            // the movie texture is the one the ps samples at the scrolled
            // coords; in the pure-color concert path it is the white dummy
            // and movie.r is 1, so dtRaw = 1 - _DepthClip.
            float depth_term()
            {
                float dt_raw = 1.0 - _DepthClip;
                float gate = (1.0 - dt_raw) >= _DepthClipZone ? 0.0 : 1.0;
                float dt = gate * (1.0 - dt_raw) + dt_raw;
                return dt * (1.0 - _PostFilmIsWithoutDepth);
            }

            // the layer color G, from the key's colorType:
            //   0 ColorAll        - color0 only (the COLOR_ONLY ps reads
            //                        cb0[155] = color0 and nothing else)
            //   1 Color2TopBottom - lerp(color0, color1) over v
            //   2 Color2LeftRight - lerp(color0, color1) over u
            //   3 Color4          - the four-corner bilinear ramp
            // songs author color1..3 as black on colorType-0 keys, so the
            // old unconditional ramp faded the whole layer to black.
            fixed4 layer_color(float2 uv)
            {
                int ct = (int)(_PostFilmColorType + 0.5);
                if (ct == 1)
                    return lerp(_PostFilmColor0, _PostFilmColor1, uv.y);
                if (ct == 2)
                    return lerp(_PostFilmColor0, _PostFilmColor1, uv.x);
                if (ct == 3)
                {
                    fixed4 g0 = lerp(_PostFilmColor0, _PostFilmColor1, uv.x);
                    fixed4 g1 = lerp(_PostFilmColor2, _PostFilmColor3, uv.x);
                    return lerp(g0, g1, uv.y);
                }
                return _PostFilmColor0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                float m = mask(i.uv);
                float dt = depth_term();
                // the mask tail (@76838): the ps computes
                //   r0.y = (1 - dt) * dt - m
                //   mask = lerp(m, r0.y, IsInverseVignette)
                // so the plain variant paints m and the inverse variant
                // paints (1-dt)*dt - m. with dt=1 the inverse term is -m,
                // which is NEGATIVE inside the radius and positive outside
                // it: the inverse layer clears the centre and rims the
                // edges. the old mad form produced dt*(1-m) instead, a
                // positive near-1.0 fill over the whole centre, so the
                // layer saturated to a white wash.
                float inv_tail = (1.0 - dt) * dt - m;
                float v = lerp(m, inv_tail, _PostFilmIsInverseVignette);
                // the layer scale: the ps multiplies the layer color by
                // cb0[152].x = _DepthPower = key.depthPower, so a layer
                // with depthPower 0 paints exactly zero.
                float p = _DepthPower;
                fixed4 g = layer_color(i.uv);

            #ifdef MODE_NONE
                return scene;
            #elif MODE_LERP
                // o = lerp(C, L, a) with L = G scaled by the power row.
                return lerp(scene, g * saturate(p), saturate(p));
            #elif MODE_ADD
                // L = G*dt*152.x*G.a; o = C + L.
                return scene + g * (p * dt) * g.a;
            #elif MODE_MUL
                // o = C + 152.x*(C*L - C) = C*(1 + p*(G-1)) with dt folded.
                return scene + p * dt * (scene * g - scene);
            #elif MODE_VIGNETTE_LERP
                // o = C + (mask*152.x)*(G - C).
                return scene + (v * p) * (g - scene);
            #elif MODE_VIGNETTE_ADD
                // the game's tail (@90722 ops 50-51):
                //   L = mask * (cb0[152].x * G.rgb); o = C + L.
                // the mask is SIGNED: the inverse variant is negative inside
                // the radius, so the layer subtracts there. saturating it
                // clamped every negative to zero and turned the subtraction
                // into a plain centre fill.
                fixed3 layer = v * (p * g.rgb);
                return fixed4(scene.rgb + layer, scene.a);
            #elif MODE_VIGNETTE_MUL
                // o = C + 152.x*mask*(C*G - C) (@104130).
                return scene + p * v * (scene * g - scene);
            #elif MODE_MONOCHROME
                // o = lerp(C, lerp(C, lum*G.rgb, G.a), c0.a) with the lum
                // third-average scaled by 152.x (no vignette preamble).
                float lum = dot(scene.rgb, float3(0.3333, 0.3333, 0.3333)) * p;
                fixed4 mg = fixed4(lerp(scene.rgb, lum.xxx * g.rgb, g.a), 1);
                return lerp(scene, mg, g.a);
            #elif MODE_SCREENBLEND
                // o = screen(C, G*mask-ish) *152.x fold: the decoded shape
                // scales the blend result by 152.x then adds the base.
                fixed4 sb = 1 - (1 - scene) * (1 - saturate(g * p));
                return lerp(scene, sb, p);
            #elif MODE_VIGNETT_SCREENBLEND
                fixed4 sb = 1 - (1 - scene) * (1 - saturate(g * (v * p)));
                return lerp(scene, sb, v * p);
            #else
                return scene;
            #endif
            }
            ENDCG
        }
    }
    Fallback Off
}
