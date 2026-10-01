using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UV2.Live
{
    // one authored AnimationCurve keyframe (time/value in 0..1, slopes).
    [Serializable]
    public class curve_key
    {
        public float time;
        public float value;
        public float in_slope;
        public float out_slope;
    }

    // one timeline key: frame + the common blend fields every track shares.
    [Serializable]
    public class live_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;

        // the key's authored AnimationCurve keyframes; the game evaluates the
        // NEXT key's curve between keys, so the blend reads this list.
        public List<curve_key> curve = new();

        // seconds position of this key on the clock.
        public float time => frame / 60f;
    }

    // camera position key (worksheet cameraPosKeys entries).
    [Serializable]
    public class camera_pos_key : live_key
    {
        public int set_type;               // 0=Direct, 1=Character
        public Vector3 position;           // direct world position
        public Vector3 pos_direct;         // extra direct-space offset
        public Vector3 offset;             // final additive offset (containOffset)
        public Vector3 chara_pos;          // offset from the character target
        public int chara_relative_base;    // 0=center group, others=formation group base
        public int chara_relative_parts;   // 21-member parts table
        public float trace_speed;
        public float near_clip;
        public float far_clip;
        public int culling_layer;

        // authored bezier control points between this key and the next.
        public List<Vector3> bezier_points = new();
    }

    // camera look-at key.
    [Serializable]
    public class camera_lookat_key : live_key
    {
        public int look_at_type;          // 0=Direct, 1=Character
        public Vector3 position;
        public int look_at_chara_pos;     // position flags: bit i enables slot i
        public int look_at_chara_parts;
        public Vector3 look_at_chara_pos_offset;  // the charaPos field

        // authored bezier control points between this key and the next.
        public List<Vector3> bezier_points = new();
    }

    // camera fov key.
    [Serializable]
    public class camera_fov_key : live_key
    {
        public int fov_type;
        public float fov;
    }

    // camera roll key.
    [Serializable]
    public class camera_roll_key : live_key
    {
        public float degree;
    }

    // camera switcher key (which camera index cuts in).
    [Serializable]
    public class camera_switcher_key : live_key
    {
        public int camera_index;
    }

    // timescale key: the clock multiplies by this rate between keys.
    [Serializable]
    public class timescale_key : live_key
    {
        public float time_scale;
    }

    // one motion-sequence key: a dance clip + its playback fields.
    [Serializable]
    public class motion_seq_key : live_key
    {
        public string motion_name;        // e.g. son1004/anm_liv_son1004_1st
        public int motion_head_frame;
        public int[] motion_head_frame_separates;
        public int play_frame_length;
        public float play_speed;
        public int use_second_motion;
        public int loop;                 // 1 = the game repeats the clip
        public int is_motion_head_frame_all;  // 1 = every character shares motion_head_frame
    }

    // formation offset key: stage placement + the mic fields ride here too.
    [Serializable]
    public class formation_key : live_key
    {
        public Vector3 position;
        public float rotation_y;
        public float local_rotation_y;
        public float scale_factor;
        public int visible;
        public int ik_system;             // 4 = mic stand
        public int ik_param1;             // node-name index (L)
        public int ik_param2;             // node-name index (R)
        public int ik_enabled_l;
        public int ik_enabled_r;
        public Vector3 ik_l_high;
        public Vector3 ik_l_low;
        public Vector3 ik_r_high;
        public Vector3 ik_r_low;
    }

    // one authored global-light key: the toon light direction for the frame.
    [Serializable]
    public class global_light_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public Vector3 light_dir;
        public Color rim_color;
        public float rim_step;
        public float rim_feather;
        public float rim_spec_rate;
        public float rim_shadow_rate;
        public Color rim_color2;
        public float rim_step2;
        public float rim_feather2;
        public float rim_spec_rate2;
        public float rim_shadow_rate2;
    }

    // cinematic animation-clip camera move (the cameraMotionKeys track):
    // an authored clip samples onto a proxy transform and the camera rides it.
    [Serializable]
    public class camera_motion_key : live_key
    {
        public bool is_enable;
        public int motion_type;
        public string clip_name;
        public float motion_head_time;
        public float play_speed;
        public int chara_relative_base;
        public int chara_relative_parts;
        public Vector3 offset;
        public Vector3 chara_pos;
    }

    public class camera_layer_key : live_key
    {
        public Vector3 offset_min_position;
        public Vector3 offset_max_position;
    }

    public class live_worksheet
    {
        public List<blink_track_container> blink_tracks = new();
        public string song_id;
        public List<global_light_key> global_light = new();
        public List<camera_pos_key> camera_pos = new();
        public List<camera_lookat_key> camera_lookat = new();
        public List<camera_fov_key> camera_fov = new();
        public List<camera_roll_key> camera_roll = new();
        public List<camera_switcher_key> camera_switcher = new();
        public List<camera_motion_key> camera_motion = new();
        public List<camera_layer_key> camera_layer = new();
        public List<timescale_key> timescale = new();
        public List<List<motion_seq_key>> motion_sequences = new();
        public Dictionary<string, List<formation_key>> formation = new();
        public float total_frames;
    }
}
