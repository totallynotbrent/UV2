using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UV2.Live;

namespace UV2.Live
{
    // plays the authored dance clips: legacy Animation component per character,
    // the timeline's own consumer pattern, with per-character start frames.
    public class motion_player : MonoBehaviour
    {
        private live_worksheet ws;
        private timeline_clock clock;
        private readonly Dictionary<int, Animation> animators = new();
        private readonly Dictionary<int, string> current_clips = new();

        // slot -> sequence index per song (from song_config_matrix motionSequenceIndices).
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

        // registers a loaded clip against one character's slot.
        public void bind_clip(int slot, AnimationClip clip)
        {
            if (!animators.TryGetValue(slot, out var anim)) return;
            if (clip == null) return;
            if (!anim.GetClip(clip.name)) anim.AddClip(clip, clip.name);
        }

        // registers a clip on every character (the common case: all slots can
        // use any of the song's sequences).
        public void bind_clip_all(AnimationClip clip)
        {
            foreach (var kv in animators) bind_clip(kv.Key, clip);
        }

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

                var anim = animators[slot];
                if (anim == null) continue;

                // the active motion key at this time
                int i = key_eval.bracket(seq_keys, t);
                if (i < 0) continue;
                var key = seq_keys[i];

                string clip_name = key.motion_name;
                if (string.IsNullOrEmpty(clip_name)) continue;
                string short_name = clip_name.Substring(clip_name.LastIndexOf('/') + 1);

                // start the clip when it first becomes active, at the authored offset
                float start_at = key.time + (key.motion_head_frame / 60f);
                if (!current_clips.TryGetValue(slot, out var playing) || playing != short_name)
                {
                    if (anim.GetClip(short_name) != null)
                    {
                        // rewind state: crossfade into the new sequence
                        if (anim.isPlaying) anim.Stop();
                        var state = anim[short_name];
                        state.time = Mathf.Max(0f, (t - start_at) * key.play_speed);
                        state.speed = key.play_speed <= 0f ? 1f : key.play_speed;
                        state.wrapMode = WrapMode.Loop;
                        anim.Play(short_name);
                        current_clips[slot] = short_name;
                    }
                }
            }
        }
    }
}
