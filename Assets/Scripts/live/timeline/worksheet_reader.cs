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
    // loads a song's worksheet from the cutt camera bundle and maps it into the runtime model.
    public static class worksheet_reader
    {
        // converts an AnimationCurve into a curve_key list; empty when the curve carries none.
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

                // prefers the data asset's timeLength; the sheet's TotalTimeLength serializes 0 for main sheets.
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

            // bgColor1: the ambient + chara tint track, one entry per named group.
            ws.bg_color1 = (sheet.bgColor1List ?? new())
                .Select(g => new bg_color1_track
                {
                    name = g.name,
                    keys = (g.keys?.thisList ?? new()).Select(k => new bg_color1_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        flags = k.flags,
                        color = k.color,
                        power = k.power,
                        scale = k.scale,
                        saturation = k.Saturation,
                        toon_dark_color = k.toonDarkColor,
                        toon_bright_color = k.toonBrightColor,
                        outline_color = k.outlineColor,
                        outline_width_power = k.outlineWidthPower,
                        color_type = k.ColorType,
                    }).ToList(),
                })
                .Where(t => t.keys.Count > 0)
                .ToList();

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
                    is_attached_to_props = k.IsAttachedToProps != 0,
                    props_index = k.PropsIndex,
                    props_attach_node_index = k.PropsAttachNodeIndex,
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
                    trace_speed = k.traceSpeed,
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

            ws.blink_tracks = (sheet.blinkLightList ?? new())
                .Select(b => new blink_track_container
                {
                    name = b.name,
                    keys = (b.keys?.thisList ?? new()).Select(k => new blink_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        power_array = (k.powerArray ?? new()).ToList(),
                        color0_array = (k.color0Array ?? new()).ToList(),
                        color1_array = (k.color1Array ?? new()).ToList(),
                        light_blend_mode = k.LightBlendMode,
                    }).ToList(),
                    pattern = (b.keys?.thisList ?? new()).FirstOrDefault()?.pattern ?? 0,
                    color_type = (b.keys?.thisList ?? new()).FirstOrDefault()?.colorType ?? 0,
                    power_min = (b.keys?.thisList ?? new()).FirstOrDefault()?.powerMin ?? 0f,
                    power_max = (b.keys?.thisList ?? new()).FirstOrDefault()?.powerMax ?? 1f,
                    loop_count = (b.keys?.thisList ?? new()).FirstOrDefault()?.loopCount ?? 0,
                    wait_time = (b.keys?.thisList ?? new()).FirstOrDefault()?.waitTime ?? 0f,
                    turn_on_time = (b.keys?.thisList ?? new()).FirstOrDefault()?.turnOnTime ?? 0f,
                    turn_off_time = (b.keys?.thisList ?? new()).FirstOrDefault()?.turnOffTime ?? 0f,
                    keep_time = (b.keys?.thisList ?? new()).FirstOrDefault()?.keepTime ?? 0f,
                    interval_time = (b.keys?.thisList ?? new()).FirstOrDefault()?.intervalTime ?? 0f,
                }).ToList();

            ws.laser_tracks = (sheet.laserList ?? new())
                .Select(l => new laser_track_container
                {
                    name = l.name,
                    object_index = l._objectIndex,
                    material_index = l._materialIndex,
                    blink = (l.keys?.thisList ?? new()).FirstOrDefault()?.blink ?? 0,
                    blink_period = (l.keys?.thisList ?? new()).FirstOrDefault()?.blinkPeriod ?? 0f,
                    keys = (l.keys?.thisList ?? new()).Select(k => new laser_key
                    {
                        frame = k.frame,
                        object_position = k.objectPosition,
                        object_rotate = k.objectRotate,
                        object_scale = k.objectScale,
                        formation = k.formation,
                        rotate = k.rotate,
                        deg_root_yaw = k.degRootYaw,
                        deg_laser_pitch = k.degLaserPitch,
                        pos_interval = k.posInterval,
                        blink = k.blink,
                        blink_period = k.blinkPeriod,
                    }).ToList(),
                }).ToList();

            ws.spot_tracks = (sheet.spotlight3dList ?? new())
                .Select(s => new spot_track_container
                {
                    name = s.name,
                    asset_name = (s.keys?.thisList ?? new()).FirstOrDefault()?.assetName ?? "",
                    keys = (s.keys?.thisList ?? new()).Select(k => new spot_key
                    {
                        frame = k.frame,
                        is_active = k.isActive,
                        color = k.color,
                        color_power = k.colorPower,
                        position = k.position,
                        rotation = k.rotation,
                        scale = k.scale,
                        character_position = k.characterPosition,
                        character_index = k.characterIndex,
                    }).ToList(),
                }).ToList();

            ws.foot_light = (sheet.charaFootLightKeys?.thisList ?? new())
                .Select(k => new foot_light_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    easing_type = k.easingType,
                    position_flag = k.positionFlag,
                    height_max_array = (k.hightMax ?? new()).ToList(),
                    light_color_array = (k.lightColor ?? new()).ToList(),
                    light_blend_mode_array = (k.LightBlendModeArray ?? new()).ToList(),
                    easing_array = (k.EasingArray ?? new()).ToList(),
                }).ToList();

            // maps the three crowd row lists into their tracks.
            ws.audience_tracks = (sheet.audienceList ?? new())
                .Select(a => new audience_track
                {
                    name = a.name,
                    object_index = a._objectIndex,
                    keys = (a.keys?.thisList ?? new()).Select(k => new audience_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        curve = read_curve(k.curve),
                        position = k.position,
                        rotate = k.rotate,
                        scale = k.scale,
                        cyalume_color = k.cyalumeColor,
                        cyalume_glow_color = k.cyalumeGlowColor,
                        cyalume_glow_color_power = k.cyalumeGlowColorPower,
                        cyalume_mask_radius = k.cyalumeMaskRadius,
                        animation_setting = k.animationSetting,
                        animation_root_index = k.animationRootIndex,
                        animation_body_region = k.animationBodyRegion,
                        animation_category = k.animationCategory,
                        animation_index = k.animationIndex,
                        animation_wrap_mode = k.animationWrapMode,
                        animation_speed = k.animationSpeed,
                        animation_offset_time = k.animationOffsetTime,
                        animation_time = k.AnimationTime,
                        use_animation_time = k.UseAnimationTime,
                    }).ToList(),
                }).ToList();

            ws.mob_groups = (sheet.MobControlKeys ?? new())
                .Select(g => new mob_cyalume_group
                {
                    name = g.name,
                    group_index = g.GroupIndex,
                    keys = (g.Keys?.thisList ?? new()).Select(k => new mob_cyalume_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        curve = read_curve(k.curve),
                        position = k.Position,
                        angle = k.Angle,
                        scale = k.Scale,
                    }).ToList(),
                }).ToList();

            ws.cyalume_groups = (sheet.CyalumeControlKeys ?? new())
                .Select(g => new mob_cyalume_group
                {
                    name = g.name,
                    group_index = g.GroupIndex,
                    keys = (g.Keys?.thisList ?? new()).Select(k => new mob_cyalume_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        curve = read_curve(k.curve),
                        position = k.Position,
                        angle = k.Angle,
                        scale = k.Scale,
                    }).ToList(),
                }).ToList();

            ws.volume_tracks = (sheet.volumeLightKeys ?? new())
                .Select(v => new volume_track_container
                {
                    name = v.name,
                    brightness_power = (v.keys?.thisList ?? new()).FirstOrDefault()?.BlinkLightBrightnessPower ?? 1f,
                    keys = (v.keys?.thisList ?? new()).Select(k => new volume_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        sun_position = k.sunPosition,
                        color1 = k.color1,
                        power = k.power,
                        komorebi = k.komorebi,
                        blur_radius = k.blurRadius,
                        color_rate = k.ColorRate,
                        enable = k.enable,
                        is_enabled_border_clear = k.isEnabledBorderClear,
                        brightness_power = k.BlinkLightBrightnessPower,
                    }).ToList(),
                }).ToList();

            ws.uv_scroll_tracks = (sheet.uvScrollLightList ?? new())
                .Select(u => new uv_scroll_track_container
                {
                    name = u.name,
                    keys = (u.keys?.thisList ?? new()).Select(k => new uv_scroll_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        mul_color0 = k.mulColor0,
                        mul_color1 = k.mulColor1,
                        color_power = k.colorPower,
                        scroll_offset_x = k.scrollOffsetX,
                        scroll_offset_y = k.scrollOffsetY,
                        scroll_speed_x = k.scrollSpeedX,
                        scroll_speed_y = k.scrollSpeedY,
                    }).ToList(),
                }).ToList();

            ws.wash_tracks = (sheet.WashLightList ?? new())
                .Select(w => new wash_track_container
                {
                    name = w.name,
                    is_all_settings = w._isAllSettings,
                    keys = (w.keys?.thisList ?? new()).Select(k => new wash_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        raycast_distance = k.RaycastDistance,
                        camera_projection_side = k.CameraProjectionSide,
                        camera_projection_color_power = k.CameraProjectionColorPower,
                    }).ToList(),
                }).ToList();

            ws.additional_tracks = (sheet.AdditionalLightList ?? new())
                .Select(a => new additional_track_container
                {
                    name = a.name,
                    keys = (a.keys?.thisList ?? new()).Select(k => new additional_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        position = k.Position,
                        rotate = k.Rotate,
                        is_enable = k.IsEnable,
                        type = k.Type,
                        range = k.Range,
                        spot_angle = k.SpotAngle,
                        indirect_multiplier = k.IndirectMultiplier,
                        shadow_type = k.ShadowType,
                        strength = k.Strength,
                        bias = k.Bias,
                        normal_bias = k.NormalBias,
                        near_plane = k.NearPlane,
                    }).ToList(),
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

            ws.handshake = (sheet.handShakeCameraKeys?.thisList ?? new())
                .Select(k => new handshake_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    power = k.power,
                    frequency = k.frequency,
                    rate = k.Rate,
                }).ToList();

            // the postfx tracks: dof, bloom/diffusion, three film layers, fog, fade.
            ws.postfx.dof = (sheet.postEffectDOFKeys?.thisList ?? new())
                .Select(k => new dof_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    easing_type = k.easingType,
                    focal_size = k.forcalSize,
                    blur_spread = k.blurSpread,
                    character = k.charactor,
                    blur_type = k.dofBlurType,
                    quality = k.dofQuality,
                    foreground_size = k.dofForegroundSize,
                    focal_point = k.dofFocalPoint,
                    smoothness = k.dofSmoothness,
                }).ToList();

            ws.postfx.bloom = (sheet.postEffectBloomDiffusionKeys?.thisList ?? new())
                .Select(k => new bloom_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    easing_type = k.easingType,
                    bloom_dof_weight = k.bloomDofWeight,
                    threshold = k.threshold,
                    intensity = k.intensity,
                    blur_size = k.BloomBlurSize,
                    blend_mode = k.BloomBlendMode,
                    diffusion_blur_size = k.diffusionBlurSize,
                    diffusion_bright = k.diffusionBright,
                }).ToList();

            Func<Cutt.LiveTimelineKeyPostFilmDataList, List<film_key>> read_film = list =>
                (list?.thisList ?? new()).Select(k => new film_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    easing_type = k.easingType,
                    film_mode = k.filmMode,
                    color_type = k.colorType,
                    power = k.filmPower,
                    offset_param = k.filmOffsetParam,
                    option_param = k.filmOptionParam,
                    color0 = k.color0,
                    color1 = k.color1,
                    color2 = k.color2,
                    color3 = k.color3,
                    depth_power = k.depthPower,
                    depth_clip = k.DepthClip,
                    roll_angle = k.RollAngle,
                    scale = k.FilmScale,
                    layer_mode = k.layerMode,
                }).ToList();
            ws.postfx.film1 = read_film(sheet.postFilmKeys);
            ws.postfx.film2 = read_film(sheet.postFilm2Keys);
            ws.postfx.film3 = read_film(sheet.postFilm3Keys);

            ws.postfx.fog = (sheet.globalFogDataLists ?? new())
                .Where(g => g?.keys?.thisList != null)
                .SelectMany(g => g.keys.thisList.Select(k => new fog_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    easing_type = k.easingType,
                    is_distance = k.isDistance,
                    start_distance = k.startDistance,
                    is_height = k.isHeight,
                    height = k.height,
                    height_density = k.heightDensity,
                    color = k.color,
                    fog_mode = k.fogMode,
                    exp_density = k.expDensity,
                    start = k.start,
                    end = k.end,
                    use_radial_distance = k.useRadialDistance,
                })).ToList();

            ws.postfx.fade = (sheet.fadeKeys?.thisList ?? new())
                .Select(k => new fade_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    easing_type = k.easingType,
                    color = k.fadeColor,
                }).ToList();

            // the tilt-shift overlay track (uv2_tiltshift_decoded.md): the
            // game gates on mode>0; mode/quality/downsample copy un-lerped,
            // blurArea/maxBlurSize/offset/roll lerp by the next-key rule.
            ws.postfx.tiltshift = (sheet.tiltShiftKeys?.thisList ?? new())
                .Select(k => new tiltshift_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    interpolate_type = k.interpolateType,
                    curve = read_curve(k.curve),
                    easing_type = k.easingType,
                    mode = k.mode,
                    quality = k.quality,
                    blur_area = k.blurArea,
                    max_blur_size = k.maxBlurSize,
                    downsample = k.downsample,
                    offset = k.offset,
                    roll = k.roll,
                }).ToList();

            // colorCorrectionDataLists carries one named entry; its keys blend
            // the rgb curves between cur/next (the game's ColorCorrectionPass
            // evaluates them into a 256-entry LUT each frame).
            ws.postfx.color_correction = (sheet.colorCorrectionDataLists ?? new())
                .Where(g => g?.keys?.thisList != null)
                .SelectMany(g => g.keys.thisList.Select(k => new color_correction_key
                {
                    frame = k.frame,
                    attribute = k.attribute,
                    enable = k.enable,
                    saturation = k.saturation,
                    mode = k.mode,
                    red_curve = k.redCurve,
                    green_curve = k.greenCurve,
                    blue_curve = k.blueCurve,
                })).ToList();

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

            // formation: one keys list per slot group in the offset set.
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

            add_props_tracks(ws, sheet);

            add_facial_tracks(ws, sheet);

            Debug.Log($"[worksheet_reader] {music_id}: {ws.camera_pos.Count} cam keys, " +
                      $"{ws.motion_sequences.Count} motion seqs, {ws.formation.Count} formation groups");
            return ws;
        }

        // maps the facial1Set + other4FacialArray slots into per-slot track sets.
        private static void add_facial_tracks(live_worksheet ws, Cutt.LiveTimelineWorkSheet sheet)
        {
            var sources = new List<Cutt.LiveTimelineFacialData>();
            if (sheet.facial1Set != null) sources.Add(sheet.facial1Set);
            foreach (var o in sheet.other4FacialArray ?? new()) sources.Add(o);
            foreach (var src in sources)
            {
                var slot = new facial_track_set();
                slot.face = (src.faceKeys?.thisList ?? new()).Select(k => new facial_face_key
                {
                    frame = k.frame, attribute = k.attribute, interpolate_type = k.interpolateType,
                    facial_id = k.facialId, weight = k.weight, speed = k.speed, time_frames = k.time,
                }).ToList();
                slot.mouth = (src.mouthKeys?.thisList ?? new()).Select(k => new facial_mouth_key
                {
                    frame = k.frame, attribute = k.attribute, interpolate_type = k.interpolateType,
                    facial_id = k.facialId, weight = k.weight, speed = k.speed,
                    time_frames = k.time, type = k.type,
                    parts = (k.facialPartsDataArray ?? new()).Select(p => new facial_part
                        { parts_id = p.FacialPartsId, weight_per = p.WeightPer }).ToList(),
                }).ToList();
                slot.eye = (src.eyeKeys?.thisList ?? new()).Select(k => new facial_eye_key
                {
                    frame = k.frame, attribute = k.attribute, interpolate_type = k.interpolateType,
                    facial_id = k.facialId, weight = k.weight, speed = k.speed, time_frames = k.time,
                    parts_l = (k.facialPartsDataArrayL ?? new()).Select(p => new facial_part
                        { parts_id = p.FacialPartsId, weight_per = p.WeightPer }).ToList(),
                    parts_r = (k.facialPartsDataArrayR ?? new()).Select(p => new facial_part
                        { parts_id = p.FacialPartsId, weight_per = p.WeightPer }).ToList(),
                }).ToList();
                slot.eyebrow = (src.eyebrowKeys?.thisList ?? new()).Select(k => new facial_eyebrow_key
                {
                    frame = k.frame, attribute = k.attribute, interpolate_type = k.interpolateType,
                    facial_id = k.facialId, weight = k.weight, speed = k.speed, time_frames = k.time,
                    parts_l = (k.facialPartsDataArrayL ?? new()).Select(p => new facial_part
                        { parts_id = p.FacialPartsId, weight_per = p.WeightPer }).ToList(),
                    parts_r = (k.facialPartsDataArrayR ?? new()).Select(p => new facial_part
                        { parts_id = p.FacialPartsId, weight_per = p.WeightPer }).ToList(),
                }).ToList();
                slot.eye_track = (src.eyeTrackKeys?.thisList ?? new()).Select(k => new facial_eyetrack_key
                {
                    frame = k.frame, attribute = k.attribute, interpolate_type = k.interpolateType,
                    target_type = k.targetType,
                    vertical_rate_per = k.verticalRatePer, horizontal_rate_per = k.horizontalRatePer,
                    speed_rate_per = k.speedRatePer, speed = k.speed,
                    direct_position = k.DirectPosition,
                }).ToList();
                slot.ear = (src.earKeys?.thisList ?? new()).Select(k => new facial_ear_key
                {
                    frame = k.frame, attribute = k.attribute, interpolate_type = k.interpolateType,
                    facial_id = k.facialId, weight = k.weight, speed = k.speed, time_frames = k.time,
                    ear_id_l = k.facialEarIdL, ear_id_r = k.facialEarIdR,
                    random_motion = k.useEarRandomMotion != 0,
                }).ToList();
                ws.facial_slots.Add(slot);
            }
            Debug.Log($"[worksheet_reader] facial slots: {ws.facial_slots.Count} " +
                      $"(keys: {string.Join(",", ws.facial_slots.Select(s => s.face.Count + s.mouth.Count + s.eye.Count + s.eyebrow.Count + s.eye_track.Count + s.ear.Count))})");
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
                    ik_param1 = k.IKSystemParam1,
                    ik_param2 = k.IKSystemParam2,
                    ik_enabled_l = k.IsEnabledIKMicStandLOffset,
                    ik_enabled_r = k.IsEnabledIKMicStandROffset,
                    ik_l_high = k.IKMicStandLOffsetHigh,
                    ik_l_low = k.IKMicStandLOffsetLow,
                    ik_r_high = k.IKMicStandROffsetHigh,
                    ik_r_low = k.IKMicStandROffsetLow,
                });
            }
            ws.formation[name] = list;
        }

        // loads the song's propsDataGroup; null when the bundle or propsSettings is absent.
        public static List<Cutt.PropsDataGroup> load_props_groups(int music_id)
        {
            try
            {
                string data_name = $"cutt/cutt_son{music_id}/data";
                var row = meta_row(data_name);
                if (row == null) return null;
                var bundle = game_assets.open(row, config.data_root);
                if (bundle == null) return null;
                var data = bundle.LoadAllAssets<Cutt.LiveTimelineData>().FirstOrDefault();
                var groups = data?.propsSettings?.propsDataGroup;
                return groups;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[worksheet_reader] props groups {music_id}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // maps the props tracks: propsList (render state) and propsAttachList (attach).
        private static void add_props_tracks(live_worksheet ws, Cutt.LiveTimelineWorkSheet sheet)
        {
            ws.props_render = (sheet.propsList ?? new())
                .Select(p => new props_render_track
                {
                    name = p.name,
                    keys = (p.keys?.thisList ?? new()).Select(k => new props_render_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        setting_flags = k.settingFlags,
                        props_id = k.propsID,
                        renderer_enable = k.rendererEnable,
                        is_visible_attached_chara_linked = k.IsVisibleAttachedCharaLinked,
                        is_emissive = k.IsEmissive,
                    }).ToList(),
                }).ToList();

            ws.props_attach = (sheet.propsAttachList ?? new())
                .Select(p => new props_attach_track
                {
                    name = p.name,
                    keys = (p.keys?.thisList ?? new()).Select(k => new props_attach_key
                    {
                        frame = k.frame,
                        attribute = k.attribute,
                        interpolate_type = k.interpolateType,
                        easing_type = k.easingType,
                        attach_joint_name = k._attachJointName,
                        copy_position_joint_name = k._copyPositionJointName,
                        setting_flags = k._settingFlags,
                        props_id = k._propsId,
                        offset_position = k._offsetPosition,
                        offset_rotate = k.OffsetRotate,
                        offset_scale = k.OffsetScale,
                        is_link_attach_bone = k.IsLinkAttachBone,
                    }).ToList(),
                }).ToList();
        }
    }
}
