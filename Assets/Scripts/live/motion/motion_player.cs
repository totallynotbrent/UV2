using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.Live;

namespace UV2.Live
{
    // poses characters by setting state.time from the timeline key and sampling directly.
    public class motion_player : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private readonly Dictionary<int, Animation> animators = new();
        private readonly Dictionary<int, string> current_clips = new();

        // slot -> sequence index (motionSequenceIndices from the cutt data asset).
        private List<int> slot_sequence_map = new();

        public void open(live_worksheet worksheet, timeline_clock timeline, List<Transform> chara_roots, List<int> sequence_map)
        {
            ws = worksheet;
            clock = timeline;
            slot_sequence_map = sequence_map ?? new List<int>();
            for (int i = 0; i < chara_roots.Count; i++)
            {
                var anim = chara_roots[i].GetComponent<Animation>();
                if (anim == null) anim = chara_roots[i].gameObject.AddComponent<Animation>();
                anim.playAutomatically = false;
                animators[i + 1] = anim;
            }
        }

        public void bind_clip(int slot, AnimationClip clip)
        {
            if (!animators.TryGetValue(slot, out var anim)) return;
            if (clip == null) return;
            if (!anim.GetClip(clip.name)) anim.AddClip(clip, clip.name);
        }

        // all slots may use any sequence, so bind the clip everywhere.
        public void bind_clip_all(AnimationClip clip)
        {
            foreach (var kv in animators) bind_clip(kv.Key, clip);
        }

        // pose every character from the worksheet's motion tracks.
        public void play()
        {
            if (ws == null || clock == null) return;
            float t = clock.time;

            for (int slot = 1; slot <= animators.Count; slot++)
            {
                int seq = slot - 1 < slot_sequence_map.Count ? slot_sequence_map[slot - 1] : 0;
                if (seq < 0 || seq >= ws.motion_sequences.Count) continue;
                var seq_keys = ws.motion_sequences[seq];
                if (seq_keys == null || seq_keys.Count == 0) continue;

                if (!animators.TryGetValue(slot, out var anim)) continue;

                int i = key_eval.bracket(seq_keys, t);
                if (i < 0) continue;
                var key = seq_keys[i];

                string clip_name = key.motion_name;
                if (string.IsNullOrEmpty(clip_name)) continue;
                string short_name = clip_name.Substring(clip_name.LastIndexOf('/') + 1);

                var state = anim[short_name];
                if (state == null) continue;

                // the head frame: the all-share flag or the per-character separates table.
                float head_frames;
                if (key.is_motion_head_frame_all != 0 || key.motion_head_frame_separates == null || slot - 1 >= key.motion_head_frame_separates.Length)
                    head_frames = key.motion_head_frame;
                else
                    head_frames = key.motion_head_frame_separates[slot - 1];
                float start = head_frames / 60f;

                // elapsed since the key at its play speed, rescaled by the timescale track when present.
                float rate = key.play_speed <= 0f ? 1f : key.play_speed;
                float interval = (t - key.time) * rate;
                if (ws.timescale.Count > 0)
                {
                    float scaled = scaled_elapsed(key.time, t, rate);
                    if (scaled >= 0f) interval = scaled;
                }

                float clip_time = start + interval;
                state.enabled = true;
                state.weight = 1f;
                state.time = key.loop != 0 ? Mathf.Repeat(clip_time, state.length) : clip_time;
                anim.Sample();
                state.enabled = false;
            }
        }

        // integrates the timescale track between the key's start and now.
        private float scaled_elapsed(float from, float to, float rate)
        {
            if (ws.timescale.Count == 0) return -1f;
            float scaled = 0f;
            float cursor = from;
            for (int i = 0; i < ws.timescale.Count; i++)
            {
                var k = ws.timescale[i];
                float kstart = k.time;
                if (kstart >= to) break;
                float kend = i + 1 < ws.timescale.Count ? ws.timescale[i + 1].time : float.MaxValue;
                float seg_end = Mathf.Min(kend, to);
                if (seg_end > cursor)
                {
                    float scale = k.time_scale <= 0f ? 1f : k.time_scale;
                    scaled += (seg_end - cursor) * scale * rate;
                    cursor = seg_end;
                }
            }
            return cursor >= to ? scaled : scaled + (to - cursor) * rate;
        }
    }
}
