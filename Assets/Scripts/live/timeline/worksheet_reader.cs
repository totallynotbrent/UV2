using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.App;

namespace UV2.Live
{
    // parses one extracted worksheet json into the runtime model.
    // the extraction writes datapack/timeline/<music_id>.json from the game's
    // LiveTimelineWorkSheet typetree; this maps the raw fields to the key types.
    public static class worksheet_reader
    {
        // loads and parses the worksheet for a song id; null when absent.
        public static live_worksheet load(int music_id)
        {
            // streaming assets first, then the release sidecar data folder.
            string path = Path.Combine(config.datapack_path, "timeline", music_id + ".json");
            if (!File.Exists(path))
            {
                string sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "timeline", music_id + ".json");
                if (!File.Exists(sidecar)) return null;
                path = sidecar;
            }
            try
            {
                string body = File.ReadAllText(path);
                var raw = parse_raw(body);
                if (raw == null) return null;
                return map(raw, music_id);
            }
            catch (Exception e)
            {
                Debug.LogError($"[worksheet_reader] {music_id}: {e.Message}");
                return null;
            }
        }

        // minimal json object model: maps to dict/list/number/string.
        private static Dictionary<string, object> parse_raw(string body)
        {
            // the extraction is machine-written json; the mini parser returns it.
            return MiniJson.Parse(body) as Dictionary<string, object>;
        }

        private static live_worksheet map(Dictionary<string, object> raw, int music_id)
        {
            var ws = new live_worksheet();
            ws.song_id = music_id.ToString();

            ws.camera_pos = key_list(raw, "cameraPosKeys", o => new camera_pos_key
            {
                frame = i(o, "frame"),
                attribute = i(o, "attribute"),
                interpolate_type = i(o, "interpolateType"),
                easing_type = i(o, "easingType"),
                set_type = i(o, "setType"),
                position = v3(o, "position"),
                chara_pos = v3(o, "charaPos"),
                chara_relative_base = i(o, "charaRelativeBase"),
                chara_relative_parts = i(o, "charaRelativeParts"),
                trace_speed = f(o, "traceSpeed"),
                near_clip = f(o, "nearClip"),
                far_clip = f(o, "farClip"),
                culling_layer = i(o, "cullingLayer"),
            });

            ws.camera_lookat = key_list(raw, "cameraLookAtKeys", o => new camera_lookat_key
            {
                frame = i(o, "frame"),
                easing_type = i(o, "easingType"),
                look_at_type = i(o, "lookAtType"),
                position = v3(o, "position"),
                look_at_chara_pos = v3(o, "lookAtCharaPos"),
                look_at_chara_parts = i(o, "lookAtCharaParts"),
            });

            ws.camera_fov = key_list(raw, "cameraFovKeys", o => new camera_fov_key
            {
                frame = i(o, "frame"),
                easing_type = i(o, "easingType"),
                fov_type = i(o, "fovType"),
                fov = f(o, "fov"),
            });

            ws.camera_roll = key_list(raw, "cameraRollKeys", o => new camera_roll_key
            {
                frame = i(o, "frame"),
                easing_type = i(o, "easingType"),
                degree = f(o, "degree"),
            });

            ws.camera_switcher = key_list(raw, "cameraSwitcherKeys", o => new camera_switcher_key
            {
                frame = i(o, "frame"),
                easing_type = i(o, "easingType"),
                camera_index = i(o, "cameraIndex"),
            });

            ws.timescale = key_list(raw, "timescaleKeys", o => new timescale_key
            {
                frame = i(o, "frame"),
                easing_type = i(o, "easingType"),
                time_scale = f(o, "timeScale"),
            });

            // motion sequences: charaMotSeqList[].keys.thisList[]
            if (raw.TryGetValue("charaMotSeqList", out var seqs_obj) && seqs_obj is List<object> seqs)
            {
                foreach (var seq_o in seqs)
                {
                    var keys = new List<motion_seq_key>();
                    if (seq_o is Dictionary<string, object> seq &&
                        seq.TryGetValue("keys", out var keys_obj) &&
                        keys_obj is Dictionary<string, object> keys_d &&
                        keys_d.TryGetValue("thisList", out var lst_obj) &&
                        lst_obj is List<object> lst)
                    {
                        foreach (var k_o in lst)
                        {
                            if (k_o is not Dictionary<string, object> o) continue;
                            keys.Add(new motion_seq_key
                            {
                                frame = i(o, "frame"),
                                easing_type = i(o, "easingType"),
                                motion_name = s(o, "motionName"),
                                motion_head_frame = i(o, "motionHeadFrame"),
                                play_frame_length = i(o, "playFrameLength"),
                                play_speed = f(o, "playSpeed"),
                                use_second_motion = i(o, "UseSecondMotion"),
                                motion_head_frame_separates = int_array(o, "motionHeadFrameSeparetes"),
                            });
                        }
                    }
                    ws.motion_sequences.Add(keys);
                }
            }

            // formation: formationOffsetSet.{group}Keys.thisList[]
            if (raw.TryGetValue("formationOffsetSet", out var fos_obj) && fos_obj is Dictionary<string, object> fos)
            {
                foreach (var kv in fos)
                {
                    if (kv.Key == "_attribute" || kv.Key == "_playMode") continue;
                    if (kv.Key == "positionTrack") continue;
                    if (kv.Key.Contains("positionPriority")) continue;
                    if (!kv.Key.EndsWith("Keys")) continue;
                    string group = kv.Key;
                    var keys = new List<formation_key>();
                    if (kv.Value is Dictionary<string, object> gd &&
                        gd.TryGetValue("thisList", out var lst_obj) && lst_obj is List<object> lst)
                    {
                        foreach (var k_o in lst)
                        {
                            if (k_o is not Dictionary<string, object> o) continue;
                            keys.Add(new formation_key
                            {
                                frame = i(o, "frame"),
                                easing_type = i(o, "easingType"),
                                position = v3(o, "Position"),
                                rotation_y = f(o, "RotationY"),
                                local_rotation_y = f(o, "LocalRotationY"),
                                scale_factor = f(o, "ScaleFactor"),
                                visible = i(o, "visible"),
                                ik_system = i(o, "IKSystem"),
                                ik_param1 = i(o, "IKSystemParam1"),
                                ik_param2 = i(o, "IKSystemParam2"),
                                ik_enabled_l = i(o, "IsEnabledIKMicStandLOffset"),
                                ik_enabled_r = i(o, "IsEnabledIKMicStandROffset"),
                                ik_l_high = v3(o, "IKMicStandLOffsetHigh"),
                                ik_l_low = v3(o, "IKMicStandLOffsetLow"),
                                ik_r_high = v3(o, "IKMicStandROffsetHigh"),
                                ik_r_low = v3(o, "IKMicStandROffsetLow"),
                            });
                        }
                    }
                    ws.formation[group] = keys;
                }
            }

            ws.total_frames = f(raw, "TotalTimeLength");
            return ws;
        }

        // key list helper: reads <track>{thisList:[...]} and maps each entry.
        private static List<T> key_list<T>(Dictionary<string, object> raw, string track, Func<Dictionary<string, object>, T> make)
        {
            var result = new List<T>();
            if (!raw.TryGetValue(track, out var track_obj) || track_obj is not Dictionary<string, object> td) return result;
            if (!td.TryGetValue("thisList", out var lst_obj) || lst_obj is not List<object> lst) return result;
            foreach (var k_o in lst)
            {
                if (k_o is Dictionary<string, object> o) result.Add(make(o));
            }
            return result;
        }

        private static int i(Dictionary<string, object> o, string k) =>
            o.TryGetValue(k, out var v) && v is long l ? (int)l : o.TryGetValue(k, out var v2) && v2 is double d ? (int)d : 0;

        private static float f(Dictionary<string, object> o, string k)
        {
            if (!o.TryGetValue(k, out var v)) return 0f;
            if (v is double d) return (float)d;
            if (v is long l) return l;
            return 0f;
        }

        private static string s(Dictionary<string, object> o, string k) =>
            o.TryGetValue(k, out var v) && v is string str ? str : "";

        private static Vector3 v3(Dictionary<string, object> o, string k)
        {
            if (!o.TryGetValue(k, out var v) || v is not Dictionary<string, object> d) return Vector3.zero;
            return new Vector3(f(d, "x"), f(d, "y"), f(d, "z"));
        }

        private static int[] int_array(Dictionary<string, object> o, string k)
        {
            if (!o.TryGetValue(k, out var v) || v is not List<object> lst) return Array.Empty<int>();
            return lst.Select(x => x is long l ? (int)l : 0).ToArray();
        }
    }
}
