using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // phase-4 postfx key types: dof, bloom/diffusion, film (3 layers), fog, fade.
    [Serializable]
    public class dof_key : live_key
    {
        public float focal_size;
        public float blur_spread;
        public int character;
        public int blur_type;
        public int quality;
        public float foreground_size;
        public float focal_point;
        public float smoothness;
    }

    [Serializable]
    public class bloom_key : live_key
    {
        public float bloom_dof_weight;
        public float threshold;
        public float intensity;
        public float blur_size;
        public int blend_mode;
        public float diffusion_blur_size;
        public float diffusion_bright;
    }

    [Serializable]
    public class film_key : live_key
    {
        public int film_mode;
        public int color_type;
        public float power;
        public Vector2 offset_param;
        public Vector4 option_param;
        public Color color0;
        public Color color1;
        public Color color2;
        public Color color3;
        public float depth_power;
        public float depth_clip;
        public float roll_angle;
        public Vector2 scale;
        public int layer_mode;
    }

    [Serializable]
    public class fog_key : live_key
    {
        public byte is_distance;
        public float start_distance;
        public byte is_height;
        public float height;
        public float height_density;
        public Color color;
        public int fog_mode;
        public float exp_density;
        public float start;
        public float end;
        public byte use_radial_distance;
    }

    [Serializable]
    public class fade_key : live_key
    {
        public Color color;
    }

    // the song's tilt-shift overlay keys (worksheet tiltShiftKeys). the game
    // gates on mode: 0 off, 1 planar, 2 radial; pass = quality*2 + (mode!=1).
    // (uv2_tiltshift_decoded.md)
    [Serializable]
    public class tiltshift_key : live_key
    {
        public int mode;
        public int quality;
        public float blur_area;
        public float max_blur_size;
        public int downsample;
        public Vector2 offset;
        public float roll;
    }

    // the color grading keys (worksheet colorCorrectionDataLists). the game's
    // ColorCorrectionPass::UpdateTextureParameter (0x7ff8e5101ca0) evaluates
    // each channel's AnimationCurve into a 256-entry rgb LUT texture and
    // SetPixels32+Apply, blended between cur/next by blendCurve (mode 0) or a
    // second 32-entry curve set (mode 1). saturation multiplies the graded
    // luma back in.
    [Serializable]
    public class color_correction_key : live_key
    {
        public int enable;
        public float saturation;
        public int mode;
        public AnimationCurve red_curve = new();
        public AnimationCurve green_curve = new();
        public AnimationCurve blue_curve = new();
    }

    // the radial blur keys (worksheet radialBlurKeys). the game's
    // RadialBlurPass::OnRadialBlur (0x7ff8e511e3f0) dispatches on
    // moveBlurType: 0 off, 1 radial, 2 radial+depth, 3 x-only, 4 y-only,
    // 5 ellipse-rotated; pass = 2*(type-1). (uv2_radialblur_decoded.md)
    [Serializable]
    public class radial_blur_key : live_key
    {
        public int move_blur_type;
        public Vector2 offset;
        public int downsample;
        public float start_area;
        public float end_area;
        public float power;
        public int iteration;
        public Vector2 ellipse_dir;
        public float roll_euler_angles;
        public float depth_power_front;
        public float depth_power_back;
        public Vector4 depth_cancel_rect;
        public float depth_cancel_blend_length;
    }

    // the postfx worksheet lists, mirrored from the cutt stub sheet.
    public class postfx_worksheet
    {
        public List<dof_key> dof = new();
        public List<bloom_key> bloom = new();
        public List<film_key> film1 = new();
        public List<film_key> film2 = new();
        public List<film_key> film3 = new();
        public List<fog_key> fog = new();
        public List<fade_key> fade = new();
        public List<tiltshift_key> tiltshift = new();
        public List<color_correction_key> color_correction = new();
        public List<radial_blur_key> radial_blur = new();
    }
}
