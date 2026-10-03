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

    // one mob/cyalume control group key: the crowd rig's transform for the frame.
    [Serializable]
    public class mob_cyalume_key : live_key
    {
        public Vector3 position;
        public Vector3 angle;
        public Vector3 scale = Vector3.one;
    }

    // one mob/cyalume control group: name + GroupIndex + the key track.
    [Serializable]
    public class mob_cyalume_group
    {
        public string name;
        public int group_index;
        public List<mob_cyalume_key> keys = new();
    }

    // one audience key: transform + cyalume tint + animation selection, the
    // decoded LiveTimelineKeyAudienceData shape.
    [Serializable]
    public class audience_key : live_key
    {
        public Vector3 position;
        public Vector3 rotate;
        public Vector3 scale = Vector3.one;
        public Color cyalume_color = Color.white;
        public Color cyalume_glow_color = Color.white;
        public float cyalume_glow_color_power = 1f;
        public float cyalume_mask_radius = 1f;
        public int animation_setting;
        public int animation_root_index;
        public int animation_body_region;
        public int animation_category;
        public int animation_index = -1;
        public int animation_wrap_mode;
        public float animation_speed = 1f;
        public float animation_offset_time;
        public float animation_time;
        public int use_animation_time;
    }

    // one audienceList entry: the crowd prefab name + the key track.
    [Serializable]
    public class audience_track
    {
        public string name;
        public int object_index;
        public List<audience_key> keys = new();
    }

    public class live_worksheet
    {
        public List<blink_track_container> blink_tracks = new();
        public List<spot_track_container> spot_tracks = new();
        public List<laser_track_container> laser_tracks = new();
        public List<audience_track> audience_tracks = new();
        public List<mob_cyalume_group> mob_groups = new();
        public List<mob_cyalume_group> cyalume_groups = new();
        public List<foot_light_key> foot_light = new();
        public List<volume_track_container> volume_tracks = new();
        public List<uv_scroll_track_container> uv_scroll_tracks = new();
        public List<wash_track_container> wash_tracks = new();
        public List<additional_track_container> additional_tracks = new();
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

        // the props rows: propsList carries per-prop render state
        // (rendererEnable by frame), propsAttachList carries the attach
        // contract (joint + position offset by frame, the hand-pickup switch).
        public List<props_render_track> props_render = new();
        public List<props_attach_track> props_attach = new();
    }

    // one propsList entry: the render/visibility key track of one prop
    // instance. settingFlags is the 1/2/4 slot bit the group targets.
    [Serializable]
    public class props_render_key : live_key
    {
        public int setting_flags;
        public int props_id;
        public byte renderer_enable;
        public byte is_visible_attached_chara_linked;
        public byte is_emissive;
    }

    [Serializable]
    public class props_render_track
    {
        public string name;
        public List<props_render_key> keys = new();
    }

    // one propsAttachList entry: which node the prop instance rides and the
    // authored position offset, per frame. the joint name switch mid-song is
    // the hand pickup (the stand's mic head transfers to Mic_Attach_00).
    [Serializable]
    public class props_attach_key : live_key
    {
        public string attach_joint_name;
        public string copy_position_joint_name;
        public int setting_flags;
        public int props_id;
        public Vector3 offset_position;
        public Vector3 offset_rotate;
        public Vector3 offset_scale;
        public byte is_link_attach_bone;
    }

    [Serializable]
    public class props_attach_track
    {
        public string name;
        public List<props_attach_key> keys = new();
    }
}
