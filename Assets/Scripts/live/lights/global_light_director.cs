// the song's global-light track, resolved per live cast member.
//
// the game runs EVERY eligible "GlobalLight" list in order and each one
// publishes its own light state (fork LiveTimelineControl.cs,
// AlterUpdate_GlobalLight). one list does not replace another wholesale:
//
//   * each list resolves ITS OWN bracketing key pair at the current frame;
//   * the NEXT key drives the blend: its interpolate_type picks linear,
//     curve or ease, and its curve is the curve that shapes the span
//     (fork CalculateInterpolationValue);
//   * the rotation blurs as a QUATERNION:
//     Quaternion.Lerp(Quaternion.Euler(from.lightDir), Quaternion.Euler(to.lightDir), ratio)
//     and the camera quaternion multiplies on the RIGHT when the CURRENT
//     key has cameraFollow;
//   * colours blur with Color.Lerp (CLAMPED), floats with the game's
//     unclamped lerp;
//   * the current key's `flags` word is the per-cast-member allow-list.
//     bit i admits roster slot i (shipping binary: the bit index is the
//     Director's live-cast list index, uv2_global_light_mask_semantics.md
//     section 6). a member outside the mask is SKIPPED, not reset - it
//     keeps whatever state it already had.
//
// so the per-slot state accumulates across the lists in list order: the
// last list whose mask admits a slot owns that slot's block, and a slot
// no list covers keeps its previous value.
using System.Collections.Generic;
using UnityEngine;

namespace UV2.Live
{
    // one roster slot's resolved global-light state.
    public class chara_light_state
    {
        public bool valid;
        // the current key's allow-list: bit i admits roster slot i.
        public int mask;
        public Quaternion rotation = Quaternion.identity;
        public Color rim_color = Color.white;
        public float rim_step;
        public float rim_feather;
        public float rim_spec_rate;
        public float rim_shadow_rate;
        public Color rim_color2 = Color.white;
        public float rim_step2;
        public float rim_feather2;
        public float rim_spec_rate2;
        public float rim_shadow_rate2;
        public float rim_horizon_offset;
        public float rim_vertical_offset;
        public float rim_horizon_offset2;
        public float rim_vertical_offset2;

        public void copy_from(chara_light_state other)
        {
            valid = other.valid;
            mask = other.mask;
            rotation = other.rotation;
            rim_color = other.rim_color;
            rim_step = other.rim_step;
            rim_feather = other.rim_feather;
            rim_spec_rate = other.rim_spec_rate;
            rim_shadow_rate = other.rim_shadow_rate;
            rim_color2 = other.rim_color2;
            rim_step2 = other.rim_step2;
            rim_feather2 = other.rim_feather2;
            rim_spec_rate2 = other.rim_spec_rate2;
            rim_shadow_rate2 = other.rim_shadow_rate2;
            rim_horizon_offset = other.rim_horizon_offset;
            rim_vertical_offset = other.rim_vertical_offset;
            rim_horizon_offset2 = other.rim_horizon_offset2;
            rim_vertical_offset2 = other.rim_vertical_offset2;
        }
    }

    public class global_light_director : MonoBehaviour
    {
        // the live cast roster: twenty slots, the game's dense cast order.
        public const int roster_slots = 20;

        private readonly List<List<global_light_key>> lists = new();
        private readonly List<chara_light_state> slots = new();

        // the most recently published rim colour, for the scene-wide
        // _GlobalRimColor the stage shaders read.
        private Color global_rim_color = Color.white;
        private bool global_rim_set;

        // the rotation of the last list that published: the scene-wide
        // fallback light direction (the shader's _MainLightPosition).
        private Quaternion global_rotation = Quaternion.identity;

        public IReadOnlyList<chara_light_state> slot_states => slots;
        public Color rim_color => global_rim_set ? global_rim_color : Color.white;
        public Quaternion scene_rotation => global_rotation;

        public void open(live_worksheet ws)
        {
            lists.Clear();
            slots.Clear();
            for (int i = 0; i < roster_slots; i++) slots.Add(new chara_light_state());

            if (ws == null) return;

            // the reader already filtered the lists to the ones the game
            // plays: named "GlobalLight" with an enabled list attribute.
            if (ws.global_light_lists != null)
            {
                foreach (var list in ws.global_light_lists)
                    if (list != null && list.Count > 0) lists.Add(list);
            }
            else if (ws.global_light != null && ws.global_light.Count > 0)
            {
                lists.Add(ws.global_light);
            }
        }

        // resolves every list at this song time and refreshes the slots
        // each list's mask admits.
        // the settings window's light override: a yaw bias in degrees and a
        // freeze switch. a frozen light ignores the song's keys and holds
        // the direction it had when the switch was thrown.
        private float yaw_bias_degrees;
        private Quaternion base_rotation = Quaternion.identity;
        private bool frozen;
        private Quaternion frozen_rotation = Quaternion.identity;

        // the light's current world direction, for the settings window to show.
        public Quaternion current_rotation => global_rotation;

        public void set_light_yaw(float degrees)
        {
            yaw_bias_degrees = degrees;
        }

        public float yaw_bias => yaw_bias_degrees;

        // freezes the light where it stands, so the toon ramps can be judged
        // against one steady key light instead of the song's moving one.
        public void set_frozen(bool value)
        {
            if (value == frozen) return;
            frozen = value;
            if (frozen) frozen_rotation = global_rotation;
        }

        public bool is_frozen => frozen;

        public void update(float song_time, Camera cam)
        {
            if (frozen)
            {
                global_rotation = frozen_rotation;
                ApplyRotationBias();
                return;
            }
            if (lists.Count == 0) return;

            var resolved = new chara_light_state();
            bool any = false;

            foreach (var keys in lists)
            {
                if (!resolve(keys, song_time, cam, resolved)) continue;
                any = true;

                int mask = resolved.mask;
                for (int slot = 0; slot < roster_slots; slot++)
                {
                    if (((mask >> slot) & 1) == 0) continue;
                    slots[slot].copy_from(resolved);
                }
            }

            if (any)
            {
                global_rim_color = resolved.rim_color;
                global_rotation = resolved.rotation;
                global_rim_set = true;
            }
            ApplyRotationBias();
        }

        // turns the light's yaw around the world's vertical axis.
        // the bias is applied to the base rotation, never to the already
        // biased value, so holding a stale frame cannot rotate the light
        // twice.
        private void ApplyRotationBias()
        {
            base_rotation = global_rotation;
            if (Mathf.Abs(yaw_bias_degrees) < 0.001f) return;
            global_rotation = Quaternion.Euler(0f, yaw_bias_degrees, 0f) * base_rotation;
        }

        // resolves one list's bracketing key pair into the light state.
        // returns false when the list has no key at this frame.
        private bool resolve(List<global_light_key> keys, float song_time, Camera cam,
            chara_light_state out_state)
        {
            int i = key_eval.bracket(keys, song_time);
            if (i < 0) return false;

            var cur = keys[i];
            var next = (i + 1 < keys.Count) ? keys[i + 1] : null;

            // the game only blends when the next key asks for it; a hold
            // (interpolate type 0) or a missing next key takes the current
            // key's values verbatim.
            bool blend = next != null && next.interpolate_type != 0;
            float ratio = blend ? ratio_for(cur, next, song_time) : 0f;

            Quaternion rot = Quaternion.Euler(cur.light_dir);
            if (blend)
            {
                rot = Quaternion.Lerp(rot, Quaternion.Euler(next.light_dir), ratio);
            }
            if (cur.camera_follow != 0 && cam != null)
            {
                rot = rot * cam.transform.rotation;
            }

            out_state.valid = true;
            out_state.rotation = rot;
            out_state.mask = cur.flags;

            out_state.rim_color = blend
                ? Color.Lerp(cur.rim_color, next.rim_color, ratio)
                : cur.rim_color;
            out_state.rim_step = blend ? lerp_unclamped(cur.rim_step, next.rim_step, ratio) : cur.rim_step;
            out_state.rim_feather = blend ? lerp_unclamped(cur.rim_feather, next.rim_feather, ratio) : cur.rim_feather;
            out_state.rim_spec_rate = blend ? lerp_unclamped(cur.rim_spec_rate, next.rim_spec_rate, ratio) : cur.rim_spec_rate;
            out_state.rim_shadow_rate = blend ? lerp_unclamped(cur.rim_shadow_rate, next.rim_shadow_rate, ratio) : cur.rim_shadow_rate;
            out_state.rim_color2 = blend
                ? Color.Lerp(cur.rim_color2, next.rim_color2, ratio)
                : cur.rim_color2;
            out_state.rim_step2 = blend ? lerp_unclamped(cur.rim_step2, next.rim_step2, ratio) : cur.rim_step2;
            out_state.rim_feather2 = blend ? lerp_unclamped(cur.rim_feather2, next.rim_feather2, ratio) : cur.rim_feather2;
            out_state.rim_spec_rate2 = blend ? lerp_unclamped(cur.rim_spec_rate2, next.rim_spec_rate2, ratio) : cur.rim_spec_rate2;
            out_state.rim_shadow_rate2 = blend ? lerp_unclamped(cur.rim_shadow_rate2, next.rim_shadow_rate2, ratio) : cur.rim_shadow_rate2;
            out_state.rim_horizon_offset = blend ? lerp_unclamped(cur.rim_horizon_offset, next.rim_horizon_offset, ratio) : cur.rim_horizon_offset;
            out_state.rim_vertical_offset = blend ? lerp_unclamped(cur.rim_vertical_offset, next.rim_vertical_offset, ratio) : cur.rim_vertical_offset;
            out_state.rim_horizon_offset2 = blend ? lerp_unclamped(cur.rim_horizon_offset2, next.rim_horizon_offset2, ratio) : cur.rim_horizon_offset2;
            out_state.rim_vertical_offset2 = blend ? lerp_unclamped(cur.rim_vertical_offset2, next.rim_vertical_offset2, ratio) : cur.rim_vertical_offset2;

            return true;
        }

        // the blend factor for a span, driven by the NEXT key's type: 1 is
        // linear, 2 uses the next key's authored curve, 3 is the easing
        // table. the ease runs in FRAME units (fork
        // EaseInterpolateKeyframes passes curFrame - from.frame and the
        // frame span as the duration).
        private static float ratio_for(global_light_key cur, global_light_key next, float song_time)
        {
            float span_frames = next.frame - cur.frame;
            if (span_frames <= 0f) return 0f;
            float cur_frame = song_time * 60f;
            float raw = Mathf.Clamp01((cur_frame - cur.frame) / span_frames);

            switch (next.interpolate_type)
            {
                case 1:
                    return raw;
                case 2:
                    if (next.native_curve != null && next.native_curve.length > 0)
                        return next.native_curve.Evaluate(raw);
                    if (next.curve != null && next.curve.Count > 0)
                        return key_eval.evaluate_curve_smooth(next.curve, raw);
                    return raw;
                case 3:
                    return Mathf.Clamp01(live_easing.evaluate(next.easing_type,
                        cur_frame - cur.frame, 0f, 1f, span_frames));
                default:
                    return 0f;
            }
        }

        // the game's LerpWithoutClamp: the blend runs past the end points.
        private static float lerp_unclamped(float a, float b, float t) => a + (b - a) * t;
    }
}
