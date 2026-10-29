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
            //   p  = ((uv*aspect - 0.5*aspect) * scale.xy), roll-rotated,
            //        recentered (+0.5, x/aspect), + _PostFilmOffsetParam
            //   q  = saturate(dot(p,p) * optionParam.x)
            //   m  = (optionParam.y > 0) ? q/(q + optionParam.y*(1-q)) : 1
            // the ramp m is 0 at the frame centre and grows toward the
            // edges, so the normal layer paints the frame edges and the
            // inverse layer fills the centre. the old port negated the
            // numerator and painted a bright centre disc instead.
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
                float q = saturate(dot(d, d) * _PostFilmOptionParam.x);
                float soft = _PostFilmOptionParam.y;
                return soft > 0.0 ? q / (q + soft * (1.0 - q)) : 1.0;
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
                // the mask: the ps computes pre = dt * m (op 48), then
                //   r0.y = (1 - m) * dt - pre     (op 49)
                //   mask = inv * r0.y + pre       (op 50)
                // so the normal variant paints dt*m (edges) and the
                // inverse variant paints dt*(1-m) (centre). the combine is
                // a mad, not a lerp: lerp(pre, y, inv) drops the +pre term
                // and yields dt*(1-2m) for the inverse variant, which goes
                // negative past mid-radius and kills the centre fill.
                float pre = dt * m;
                float y = dt * (1.0 - m) - pre;
                float v = _PostFilmIsInverseVignette * y + pre;
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
                //   L = saturate(mask * (cb0[152].x * G.rgb)); o = C + L.
                fixed3 layer = saturate(v * (p * g.rgb));
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
