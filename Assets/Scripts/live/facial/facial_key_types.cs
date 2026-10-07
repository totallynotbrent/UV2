// facial key types mirroring the game's cutt worksheet facial tracks.
// semantics from ~/umadump/out/uv2_facial_blend_decoded.md: rate r = clamp(speed*0.01, 0, 1)
// applied as a per-category intensity multiplier (w = base * r), hard-cut on id change,
// silent no-op on unknown ids. five categories per slot: eyeR, eyeL, eyebrowR, eyebrowL, mouth.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // one authored morph mix entry: 1-based index into the face target's
    // per-category morph list, plus a percent weight (x0.01).
    [Serializable]
    public class facial_part
    {
        public int parts_id;
        public int weight_per;
    }

    // face/eye/eyebrow/ear share the plain key shape; mouth adds the type selector.
    [Serializable]
    public class facial_face_key : live_key
    {
        public int facial_id;
        public int weight;      // percent 0..100
        public int speed;       // percent 0..100 -> rate = clamp(speed*0.01, 0, 1)
        public int time_frames; // duration in frames @60
    }

    [Serializable]
    public class facial_mouth_key : live_key
    {
        public int facial_id;
        public int weight;
        public int speed;
        public int time_frames;
        public int type;
        public List<facial_part> parts = new();  // the morph mix (1-based ids)
    }

    [Serializable]
    public class facial_eye_key : live_key
    {
        public int facial_id;
        public int weight;
        public int speed;
        public int time_frames;
        public List<facial_part> parts_l = new();
        public List<facial_part> parts_r = new();
    }

    [Serializable]
    public class facial_eyebrow_key : facial_eye_key
    {
    }

    // gaze: rates are percent; DirectPosition is the look target for direct mode.
    [Serializable]
    public class facial_eyetrack_key : live_key
    {
        public int target_type;
        public int vertical_rate_per;
        public int horizontal_rate_per;
        public int speed_rate_per;
        public int speed;
        public Vector3 direct_position;
    }

    // ear keys: id-driven (EarType/2) per side, not a trs parts array
    // (census §4 - the face target carries no ear list).
    [Serializable]
    public class facial_ear_key : live_key
    {
        public int facial_id;
        public int weight;
        public int speed;
        public int time_frames;
        public int ear_id_l;
        public int ear_id_r;
        public bool random_motion;
    }

    // per-slot facial bundle: the game's LiveTimelineFacialData.
    [Serializable]
    public class facial_track_set
    {
        public List<facial_face_key> face = new();
        public List<facial_mouth_key> mouth = new();
        public List<facial_eye_key> eye = new();
        public List<facial_eyebrow_key> eyebrow = new();
        public List<facial_eyetrack_key> eye_track = new();
        public List<facial_ear_key> ear = new();
    }
}
