using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UV2.App;
using UV2.Data;
using Cutt = Gallop.Live.Cutt;

namespace UV2.Live
{
    // loads one song's LiveTimelineWorkSheet from the game's cutt camera bundle,
    // deserialized by the generated stub, and maps it into the runtime model.
    public static class worksheet_reader
    {
        // the key's authored AnimationCurve as curve_key list; empty when the
        // game ships the curve container with no keyframes.
        private static List<curve_key> read_curve(AnimationCurve curve)
        {
            var keys = new List<curve_key>();
            if (curve == null) return keys;
            foreach (var kf in curve.keys)
            {
                keys.Add(new curve_key { time = kf.time, value = kf.value, in_slope = kf.inTangent, out_slope = kf.outTangent });
            }
            return keys;
        }

        // loads and maps the worksheet for a song id; null when absent.
        public static live_worksheet load(int music_id)
        {
            try
            {
                var sheet = load_stub(music_id);
                if (sheet == null) return null;
                var ws = map(sheet, music_id);

                // the data asset's timeLength (seconds) is the authoritative
                // duration; the sheet's TotalTimeLength serializes 0 on disk
                // for main sheets, so it only overrides when valid.
                int time_length = load_data_time_length(music_id);
                if (time_length > 0)
                {
                    if (sheet.TotalTimeLength > 1f && sheet.TotalTimeLength <= time_length)
                        ws.total_frames = sheet.TotalTimeLength * 60f;
                    else
                        ws.total_frames = time_length * 60f;
                }
                return ws;
            }
            catch (Exception e)
            {
                Debug.LogError($"[worksheet_reader] {music_id}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // loads the song's cutt data asset and returns its authored length.
        private static int load_data_time_length(int music_id)
        {
            string data_name = $"cutt/cutt_son{music_id}/data";
            var row = meta_row(data_name);
            if (row == null) return 0;
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return 0;
            var data = bundle.LoadAllAssets<Cutt.LiveTimelineData>().FirstOrDefault();
            return data?.timeLength ?? 0;
        }

        // opens the song's camera cutt bundle and deserializes the worksheet stub.
        private static Cutt.LiveTimelineWorkSheet load_stub(int music_id)
        {
            string bundle_name = $"cutt/cutt_son{music_id}/son{music_id}_camera";
            var row = meta_row(bundle_name);
            if (row == null)
            {
                foreach (var cand in new[]
                         {
                             $"cutt/cutt_son{music_id}/cutt_son{music_id}_camera",
                             $"cutt/cutt_son{music_id}_camera",
                         })
                {
                    row = meta_row(cand);
                    if (row != null) break;
                }
                if (row == null)
                {
                    Debug.LogWarning($"[worksheet_reader] no camera bundle for song {music_id}");
                    return null;
                }
            }
            var bundle = game_assets.open(row, config.data_root);
            if (bundle == null) return null;
            var sheet = bundle.LoadAllAssets<Cutt.LiveTimelineWorkSheet>().FirstOrDefault();
            if (sheet == null)
                Debug.LogWarning($"[worksheet_reader] no worksheet bound in {row.name}");
            return sheet;
        }

        private static meta_reader.asset_row meta_row(string name)
        {
            using var meta = meta_reader.reader.open(config.meta_db_path);
            var rows = meta?.lookup(new HashSet<string> { name });
            return rows?.GetValueOrDefault(name);
        }

        // maps the deserialized stub into the runtime key model.
        private static live_worksheet map(Cutt.LiveTimelineWorkSheet sheet, int music_id)
        {
            var ws = new live_worksheet();
            ws.song_id = music_id.ToString();
            ws.total_frames = sheet.TotalTimeLength * 60f;

            ws.global_light = (sheet.globalLightDataLists?.FirstOrDefault()?.keys?.thisList ?? new())
                .Select(k => new global_light_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    easing_type = k.easingType,
                    light_dir = k.lightDir,
                    rim_color = k.rimColor,
                    rim_step = k.rimStep,
                    rim_feather = k.rimFeather,
                    rim_spec_rate = k.rimSpecRate,
                    rim_shadow_rate = k.globalRimShadowRate,
                    rim_color2 = k.rimColor2,
                    rim_step2 = k.rimStep2,
                    rim_feather2 = k.rimFeather2,
                    rim_spec_rate2 = k.rimSpecRate2,
                    rim_shadow_rate2 = k.globalRimShadowRate2,
                }).ToList();

            ws.camera_pos = (sheet.cameraPosKeys?.thisList ?? new())
                .Select(k => new camera_pos_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    easing_type = k.easingType,
                    curve = read_curve(k.curve),
                    set_type = k.setType,
                    position = k.position,
                    pos_direct = k.posDirect,
                    offset = k.offset,
                    chara_pos = k.charaPos,
                    chara_relative_base = k.charaRelativeBase,
                    chara_relative_parts = k.charaRelativeParts,
                    trace_speed = k.traceSpeed,
                    bezier_points = (k.bezierPoints ?? new()).ToList(),
                    near_clip = k.nearClip,
                    far_clip = k.farClip,
                    culling_layer = k.cullingLayer,
                }).ToList();

            ws.camera_lookat = (sheet.cameraLookAtKeys?.thisList ?? new())
                .Select(k => new camera_lookat_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    easing_type = k.easingType,
                    curve = read_curve(k.curve),
                    look_at_type = k.lookAtType,
                    position = k.position,
                    look_at_chara_pos = k.lookAtCharaPos,
                    look_at_chara_parts = k.lookAtCharaParts,
                    look_at_chara_pos_offset = k.charaPos,
                    bezier_points = (k.bezierPoints ?? new()).ToList(),
                }).ToList();

            ws.camera_fov = (sheet.cameraFovKeys?.thisList ?? new())
                .Select(k => new camera_fov_key
                {
                    frame = k.frame,
                    easing_type = k.easingType,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    fov_type = k.fovType,
                    fov = k.fov,
                }).ToList();

            ws.camera_layer = (sheet.cameraLayerKeys?.thisList ?? new())
                .Select(k => new camera_layer_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    easing_type = k.easingType,
                    offset_min_position = k.offsetMinPosition,
                    offset_max_position = k.offsetMaxPosition,
                }).ToList();

            ws.camera_roll = (sheet.cameraRollKeys?.thisList ?? new())
                .Select(k => new camera_roll_key
                {
                    frame = k.frame,
                    easing_type = k.easingType,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    degree = k.degree,
                }).ToList();

            ws.camera_motion = (sheet.cameraMotionKeys?.thisList ?? new())
                .Select(k => new camera_motion_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    is_enable = k.IsEnable != 0,
                    motion_type = k.MotionType,
                    clip_name = k.Clip != null ? k.Clip.name : null,
                    motion_head_time = k.MotionHeadTime,
                    play_speed = k.PlaySpeed > 0f ? k.PlaySpeed : 1f,
                    chara_relative_base = k.CharaRelativeBase,
                    chara_relative_parts = k.CharaRelativeParts,
                    offset = k.Offset,
                    chara_pos = k.CharaPos,
                }).ToList();

            ws.timescale = (sheet.timescaleKeys?.thisList ?? new())
                .Select(k => new timescale_key
                {
                    frame = k.frame,
                    easing_type = 0,
                    time_scale = k.Timescale <= 0f ? 1f : k.Timescale,
                }).ToList();

            // motion sequences: one list per charaMotSeqList entry.
            foreach (var seq in sheet.charaMotSeqList)
            {
                var keys = new List<motion_seq_key>();
                foreach (var k in seq?.keys?.thisList ?? new())
                {
                    keys.Add(new motion_seq_key
                    {
                        frame = k.frame,
                        easing_type = k.easingType,
                        motion_name = k.motionName,
                        motion_head_frame = k.motionHeadFrame,
                        play_frame_length = k.playFrameLength,
                        play_speed = k.playSpeed <= 0f ? 1f : k.playSpeed,
                        use_second_motion = k.UseSecondMotion,
                        loop = k.loop,
                        is_motion_head_frame_all = k.isMotionHeadFrameAll,
                        motion_head_frame_separates = (k.motionHeadFrameSeparetes ?? new()).ToArray(),
                    });
                }
                ws.motion_sequences.Add(keys);
            }

            // formation: each slot group in the set shares one entry type.
            var fos = sheet.formationOffsetSet;
            if (fos != null)
            {
                add_formation(ws, "center", fos.centerKeys);
                add_formation(ws, "left1", fos.left1Keys);
                add_formation(ws, "right1", fos.right1Keys);
                add_formation(ws, "left2", fos.left2Keys);
                add_formation(ws, "right2", fos.right2Keys);
                add_formation(ws, "place06", fos.place06Keys);
                add_formation(ws, "place07", fos.place07Keys);
                add_formation(ws, "place08", fos.place08Keys);
                add_formation(ws, "place09", fos.place09Keys);
                add_formation(ws, "place10", fos.place10Keys);
                add_formation(ws, "place11", fos.place11Keys);
                add_formation(ws, "place12", fos.place12Keys);
                add_formation(ws, "place13", fos.place13Keys);
                add_formation(ws, "place14", fos.place14Keys);
                add_formation(ws, "place15", fos.place15Keys);
                add_formation(ws, "place16", fos.place16Keys);
                add_formation(ws, "place17", fos.place17Keys);
                add_formation(ws, "place18", fos.place18Keys);
                add_formation(ws, "place19", fos.place19Keys);
                add_formation(ws, "place20", fos.place20Keys);
            }

            Debug.Log($"[worksheet_reader] {music_id}: {ws.camera_pos.Count} cam keys, " +
                      $"{ws.motion_sequences.Count} motion seqs, {ws.formation.Count} formation groups");
            return ws;
        }

        // maps one formation group's keys into the model.
        private static void add_formation(live_worksheet ws, string name,
            Cutt.LiveTimelineKeyFormationOffsetDataList group)
        {
            if (group?.thisList == null || group.thisList.Count == 0) return;
            var list = new List<formation_key>();
            foreach (var k in group.thisList)
            {
                list.Add(new formation_key
                {
                    frame = k.frame,
                    easing_type = k.easingType,
                    position = k.Position,
                    rotation_y = k.RotationY,
                    local_rotation_y = k.LocalRotationY,
                    scale_factor = k.ScaleFactor <= 0f ? 1f : k.ScaleFactor,
                    visible = k.visible,
                    ik_system = k.IKSystem,
                });
            }
            ws.formation[name] = list;
        }
    }
}
