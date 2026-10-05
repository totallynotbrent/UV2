// facial key types mirroring the game's cutt worksheet facial tracks.
// semantics from ~/umadump/out/uv2_facial_blend_decoded.md: rate r = clamp(speed*0.01, 0, 1)
// applied as a per-category intensity multiplier (w = base * r), hard-cut on id change,
// silent no-op on unknown ids. five categories per slot: eyeR, eyeL, eyebrowR, eyebrowL, mouth.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
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
    }

    [Serializable]
    public class facial_eye_key : live_key
    {
        public int facial_id;
        public int weight;
        public int speed;
        public int time_frames;
    }

    [Serializable]
    public class facial_eyebrow_key : live_key
    {
        public int facial_id;
        public int weight;
        public int speed;
        public int time_frames;
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

    [Serializable]
    public class facial_ear_key : live_key
    {
        public int facial_id;
        public int weight;
        public int speed;
        public int time_frames;
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
