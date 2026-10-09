using System;
using System.Collections.Generic;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // drives the worksheet's chara foot light track: one upward spot light at each authored character's feet.
    public static class foot_light
    {
        public const int slot_count = 20;

        // one spot light per live chara position slot.
        private static readonly Light[] slots = new Light[slot_count];

        // the trace report: how many slots the authored keys actually claim.
        public static int authored_slots { get; private set; }

        // creates the slot light lazily under the character root.
        private static Light ensure_light(int index, List<Transform> chara_roots)
        {
            var existing = slots[index];
            if (existing != null) return existing;
            if (chara_roots == null || index >= chara_roots.Count || chara_roots[index] == null)
                return null;
            var host = new GameObject($"foot_light_{index}");
            host.transform.SetParent(chara_roots[index], false);
            host.transform.localPosition = Vector3.zero;
            // the fork parents the spot with no rotation: it inherits the
            // character's facing like the game's foot light does.
            host.transform.localRotation = Quaternion.identity;
            var light = host.AddComponent<Light>();
            light.type = LightType.Spot;
            light.shadows = LightShadows.None;
            light.spotAngle = 70f;
            light.range = 3f;
            slots[index] = light;
            return light;
        }

        // samples the shared key list and publishes every authored slot once the transforms are posed.
        public static void update(float time_sec, List<foot_light_key> keys, List<Transform> chara_roots)
        {
            if (keys == null || keys.Count == 0) return;
            float frame = time_sec * 60f;

            foot_light_key a, b;
            float blend;
            if (frame <= keys[0].frame) { a = b = keys[0]; blend = 0f; }
            else if (frame >= keys[keys.Count - 1].frame) { a = b = keys[keys.Count - 1]; blend = 0f; }
            else
            {
                a = b = keys[keys.Count - 1]; blend = 0f;
                for (int i = 0; i < keys.Count - 1; i++)
                {
                    if (frame >= keys[i].frame && frame < keys[i + 1].frame)
                    {
                        a = keys[i]; b = keys[i + 1];
                        float span = b.frame - a.frame;
                        blend = span <= 0 ? 0f : (frame - a.frame) / span;
                        break;
                    }
                }
            }

            // the fork's dispatch (Director.OnUpdateCharaFootLight): element[0]
            // of the arrays drives ONE character per key - the first set bit
            // of the position flag - with no up-aim rotation (the spot rides
            // the character's facing). multi-slot glow over-lights the stage
            // vs the game, so match the fork exactly.
            int slot = first_flag_bit(a.position_flag);
            if (slot >= 0)
            {
                var light = ensure_light(slot, chara_roots);
                if (light != null)
                {
                    float height = Mathf.Lerp(a.height(0), b.height(0), blend);
                    Color color = Color.Lerp(a.color(0), b.color(0), blend);
                    light.color = color;
                    light.intensity = Mathf.Clamp01(height) * 2f;
                    light.range = 3f;
                    light.enabled = height > 0.001f;
                }
            }

            // every other previously-lit slot must go dark this frame: the
            // game's single-slot dispatch means a stale slot from an earlier
            // key keeps burning otherwise.
            for (int i = 0; i < slot_count; i++)
            {
                if (i == slot) continue;
                if (slots[i] != null) slots[i].enabled = false;
            }
        }

        // the fork's PositionFlagToIndex: the first set bit of the flag.
        private static int first_flag_bit(long flag)
        {
            if (flag == 0) return -1;
            for (int i = 0; i < slot_count; i++)
                if ((flag & (1L << i)) != 0) return i;
            return -1;
        }

        // the bind report: slots claimed across the whole track + key count.
        public static void bind(List<foot_light_key> keys)
        {
            authored_slots = 0;
            if (keys == null)
            {
                trace_log.write("foot lights: no authored track");
                return;
            }
            long all_flags = 0;
            foreach (var k in keys)
                for (int i = 0; i < slot_count; i++)
                    if ((k.position_flag & (1 << i)) != 0) all_flags |= 1L << i;
            int count = 0;
            for (int i = 0; i < slot_count; i++)
                if ((all_flags & (1L << i)) != 0) count++;
            authored_slots = count;
            trace_log.write($"foot lights: {keys.Count} keys, {count}/{slot_count} slots authored");
        }

        // clears the slot lights (a new song rebinds).
        public static void reset()
        {
            for (int i = 0; i < slot_count; i++)
            {
                if (slots[i] != null) UnityEngine.Object.Destroy(slots[i].gameObject);
                slots[i] = null;
            }
            authored_slots = 0;
        }
    }

    // one worksheet charaFootLight key: the 20-slot arrays.
    [Serializable]
    public class foot_light_key
    {
        public int frame;
        public int attribute;
        public int interpolate_type;
        public int easing_type;
        public int position_flag;
        public List<float> height_max_array = new();
        public List<Color> light_color_array = new();
        public List<int> light_blend_mode_array = new();
        public List<int> easing_array = new();

        // per-slot accessors with fixed fallbacks when the arrays ship short.
        public float height(int slot) =>
            height_max_array != null && slot < height_max_array.Count ? height_max_array[slot] : 1f;
        public Color color(int slot) =>
            light_color_array != null && slot < light_color_array.Count ? light_color_array[slot] : Color.white;
        public int blend_mode(int slot) =>
            light_blend_mode_array != null && slot < light_blend_mode_array.Count ? light_blend_mode_array[slot] : 0;
        public int easing(int slot) =>
            easing_array != null && slot < easing_array.Count ? easing_array[slot] : 0;
    }
}
