// screen-space light shafts.
//
// the game renders its shafts as beam geometry: the stage controller's
// update-volume-light routine moves a scene transform to the authored sun
// position (it calls transform.setlocalposition with the volume light
// update info's position) and the sun shafts pass composites beam buffers
// around it.
//
// the volume data for song 1004 drives that transform with 86 keys carrying
// a sun position, a colour, a power, a blur radius and a komorebi rate, and
// the concert's washed-out highlights are that colour spread across the
// frame. this shader stands in for the beam geometry with the same maths
// the game's sun shafts composite uses:
//
//   - the bright pass subtracts the authored black level, so only what is
//     above the floor scatters;
//   - the radial march accumulates the bright pass along the ray toward
//     the projected sun, scaled by the authored blur radius;
//   - the composite spreads that along the radius with an inverse-distance
//     falloff and tints it with the authored colour.
//
// the pivot is the world's sun position seen through the camera, so the
// shafts point at the light rather than at a fixed screen centre.
Shader "live/uv2_shafts"
{
    Properties
    {
        _MainTex ("source", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            // pass 0: the bright pass and the radial march toward the sun
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            // x intensity, y density, z black level, w blur radius
            float4 _ShaftsParam;
            float2 _ShaftsPivot;   // screen-space uv pivot
            float4 _MainTex_TexelSize;
            float _ShaftsBeamSpread;
            float _ShaftsHazeAmount;
            float _ShaftsCenterMultiplex;
            float _ShaftsAspect;
            float4 _ShaftsTintA;
            float4 _ShaftsTintB;

            float3 bright_sample(float2 uv)
            {
                float3 c = tex2D(_MainTex, uv).rgb;
                float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
                // the black level is the floor the game treats as scatterable,
                // and the sun's centre brightness lifts what passes it.
                float over = max(lum - _ShaftsParam.z, 0.0);
                return c * over / max(lum, 1e-4);
            }

            // the beam's own haze: the game's shafts are geometry with an
            // authored opacity, so the cone is visible against the dark
            // parts of the frame, not only where the frame is bright.
            float3 beam_haze(float2 uv)
            {
                float2 d = (uv - _ShaftsPivot) * float2(_ShaftsAspect, 1);
                float r = max(length(d), 1e-3);
                float falloff = 1.0 / (1.0 + r * _ShaftsBeamSpread);
                return float3(falloff, falloff, falloff);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                // the march step is the ray from the pixel to the projected
                // sun, scaled by the authored blur radius.
                float2 delta = (_ShaftsPivot - i.uv) * _ShaftsParam.w * 0.05;
                float3 acc = 0;
                acc += bright_sample(i.uv);
                acc += bright_sample(i.uv + delta * 0.25);
                acc += bright_sample(i.uv + delta * 0.5);
                acc += bright_sample(i.uv + delta * 0.75);
                acc += bright_sample(i.uv + delta);
                // the marching samples carry the emissive shapes; the haze
                // carries the beam's spread.
                float3 haze = beam_haze(i.uv);
                // the sun's centre brightness and multiplex come from the
                // song's own sun shafts settings.
                haze *= _ShaftsHazeAmount * _ShaftsCenterMultiplex;
                // the beam pools around the light rather than filling the
                // frame: the falloff tapers the haze with distance so it
                // concentrates near the sun and thins toward the edges.
                float2 hd = (i.uv - _ShaftsPivot) * float2(_ShaftsAspect, 1);
                float hr = length(hd);
                haze *= 1.0 / (1.0 + hr * _ShaftsBeamSpread);
                // the game's beams carry the sun's colour in their geometry,
                // so both the emissive shapes and the haze take the tint.
                float3 tint = _ShaftsTintA.rgb;
                return float4(acc * 0.2 * tint + haze * tint, 1);
            }
            ENDCG
        }

        Pass
        {
            // pass 1: the composite, spreading the beam over the radius
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _ShaftsTex;
            // x intensity, y density, z falloff floor, w blend
            float4 _ShaftsParam;
            float2 _ShaftsPivot;
            float _ShaftsAspect;
            float4 _ShaftsTintA;
            float4 _ShaftsTintB;
            float _ShaftsBlend;
            float _ShaftsSpread;   // how quickly the beam falls off outward
            float _ShaftsBlendMode;

            float4 frag(v2f_img i) : SV_Target
            {
                float3 base = tex2D(_MainTex, i.uv).rgb;
                // the radial distance from the projected sun, aspect corrected.
                float2 d = (i.uv - _ShaftsPivot) * float2(_ShaftsAspect, 1);
                float r = max(length(d), _ShaftsParam.z);
                // the inverse-distance spread, softened so a wide beam stays
                // wide instead of collapsing to a hot core.
                float falloff = 1.0 / (1.0 + r * _ShaftsSpread);
                // the marched buffer carries the beam's structure in the
                // sun's colour, so the composite only spreads it over the
                // radius and adds it to the frame. the game's composite
                // mixes toward the effect colour on the shaft term alone,
                // so the frame behind keeps its own contrast.
                float3 marched = tex2D(_ShaftsTex, i.uv).rgb;
                float3 shafts = marched * falloff * _ShaftsParam.x;
                // the game's screen blend mode: 0 blends normally, 1 adds the
                // shafts onto the frame, 2 screens them over it.
                if (_ShaftsBlendMode > 1.5)
                    return float4(1.0 - (1.0 - saturate(base)) * (1.0 - saturate(shafts)), 1);
                return float4(base + shafts, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
