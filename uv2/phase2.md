# phase 2 — the 3d dancing stage on a timeline (no post effects)

status: in progress (restructured 09-28 with hard acceptance gates).
end goal context: 1:1 live concert. this phase delivers the core: stage
loaded, cast assembled, the authored dance on the authored camera, with
formations, mics, and base character shading. the frame is pre-postfx by
design.

## how to build this (umaviewer v1 inspiration, structure only — no code copied)

open and read these v1 modules before writing ours; ours follow their
SHAPE (load order, driver wiring, update loop), never their code:

- ~/Bots/UmaViewer/Assets/Scripts/umamusume/Gallop/Live/Director.cs (2451 L)
  — the whole boot order: Initialize -> InitializeTimeline ->
  InitializeCamera; our stage_loader.open() mirrors this sequence.
- umamusume/Gallop/Live/Cutt/LiveTimeLine/LiveTimelineControl.cs (5209 L)
  — the timeline consumer: how key tracks bracket, how the sheet drives
  per-frame updates, how missing tracks degrade.
- umamusume/Gallop/Live/StageCameraTimelineDriver.cs +
  StageTransformDriver.cs — driver wiring per system (one MonoBehaviour
  per consumer family, LateUpdate over the clock).
- umamusume/Gallop/Live/LivePropsEvaluator.cs — prop attach + slot
  placement (the mic-prop row's shape).
- UmaViewerBuilder.cs (1409 L) — cast assembly: bundle prereq order
  (shaders before materials, body before head), the exact ordering bug
  class we hit and fixed.


all camera/motion blockers are settled. decode docs:
out/camera_motion_questions_decoded.md (enums {0=Direct,1=Character},
motionHeadFrameSeparetes), out/chara_relative_parts_decoded.md (the full
21-member parts table), out/outline_width_system_decoded.md (LOD),
out/lightmap_defaults_decoded.md (base shading), mic docs (below).

## module table — the phase closes when every row is REAL

| # | module | gate | status 09-30 (audit) |
|---|---|---|---|
| 1 | worksheet runtime read: cutt camera bundle -> stub-deserialized LiveTimelineWorkSheet -> key model | all songs load, zero json sidecars | DONE — fresh all-songs run 51/51 bound, no sidecars |
| 2 | stage load + geo via manifest (controller bundle + repack-or-live read) | stage roots resolved per song; missing stages show a visible notice, never a silent swap | DONE — 49/51 roots (1157/1193 = the 2 documented-missing stages, notice path); 1157/1193 open |
| 3 | cast assembly + prereq-first materials + game shaders via shader_manager | all songs' default casts assemble with the game's own shaders | FIXED + PROVEN (c11ffdc): the chara-specific path returns folder+'/'+prefab so 160/160 slots resolve; the fresh podman run of song 1012 (the worst pre-fix regression) went 0/18 -> 18/18 cast with concert open SUCCESS on the current build. mini-path removal also cost the 17 mini songs their chibi bodies. the full 51-song gate artifact needs a re-run on the fixed build (the 09-30 run output predates c11ffdc) |
| 4 | motion playback: charaMotSeqList -> per-slot clips, motionHeadFrameSeparetes starts, case-insensitive resolve | dance plays for all songs with motion bundles | DONE — 51/51 clips load, zero-clip none |
| 5 | authored camera chain: pos/lookAt/fov/roll/switcher + cameraMotion clips + charaRelativeParts follow | camera matches authored targets at sampled frames | DONE 09-30 — v1's actual pipeline (switcher safe-cut, motion proxy sampling, aspect fov); parts table verified enum-exact vs the decode doc (17-19 Initial* unhandled, never stored = zero-impact); 15/16 POSITION variants return raw root position (doc says face-based) — unreachable in stored data, note for later |
| 6 | formation position track (group keys -> slots) | formations move on key frames | landed; heartbeat shows formation groups bound |
| 7 | mic stand IK (IKSystem=4, two-threshold hysteresis, per-chara rate) + handheld mic props | mic IK engages only on IKSystem=4 keys; prop prefab attaches | NOT implemented (ik_system parsed only) |
| 8 | base character shading (lightmap defaults publish set + LOD) | publish set applied; LOD distance gate live | MOSTLY — global_shade publishes every frame incl. chara block (rim/lightDir); per-chara SetOutlineWidthForPower absent |
| 9 | facial1Set parse + store, base face pose (blend consumer gated) | keys parse + hold base pose; no guessed blend | MOVED TO PHASE 4 (user 10-02 'i dont care about that much'): the stub parses 19 facial fields, nothing consumes them; the facialId blend decode gates this row's consumer |
| 10 | audio: oke instrumental + per-chara vocal mixes, clock drives from audio time | music audible + in sync | PARTIAL — oke instrumental plays (48/51, no-bank 1093/1175/1193 = DLC-class absence), clock audio-clocked with garbage-guard. per-chara vocals MOVED TO PHASE 4 (user 10-02) |

phase-2 acceptance evidence: Logs/all_songs_run.json re-ran 10-02 on the
fixed build: 51/51 open, 0 failed-or-missed, 48/51 music (no-bank
1093/1175/1193 = the documented DLC-class absence) — row 3's regression
gate is CLOSED by that artifact. rows 7/9 and the vocal half of 10 moved
to phase 4 (user 10-02); the phase-3 light/crowd rows landed early
(5a56955/2ca2577/1def6a1/04cf032/90a36a6/3f151cd).

## stage

- bundle: cutt/cutt_son{song} (camera+worksheet) + stage from livesettings
  type=1 param1 -> `3d/env/live/live{param1}/`. 59 stages on disk, missing:
  live10146 (song 1157), live10154 (1193/3193). visible notice + fallback,
  never a silent swap.
- geo: the controller bundle lists only its shell in the container; the
  full stage roots come from the game's own bundle set via the manifest,
  or a repack regenerated from the user's install by the release pipeline.
  shipping stale repack blobs in the repo is a shortcut violation.

## motion

- dance: worksheet charaMotSeqList holds the sequences (son1004: 1st/2nd/
  3rd/L/R, each with motionName `son1004/anm_liv_son1004_1st`,
  playFrameLength 7360, playSpeed, UseSecondMotion).
- slot->sequence map: motionSequenceIndices from the game's own serialized
  LiveTimelineData (cutt stub reads it at runtime; a member_count-length
  array; son1004: [0,1,2,3,4,3,4,...] — slots 5+ alternate L/R). 39
  distinct patterns across songs; read, never derive.
- clip bundles: `3d/motion/live/body/son{song}/anm_liv_son{song}_{seq}.unity3d`,
  60/62 on disk (3180/3193 are jingles). case quirk: worksheet says
  `anm_liv_son1004_L`/`_R`, disk is `_l`/`_r` — resolve case-insensitively.
- per-character start frames (SETTLED): isMotionHeadFrameAll=true -> all
  start at motionHeadFrame/60s; false -> chara i starts at
  motionHeadFrameSeparetes[i]/60s. same timescale integration as the
  NLB fix (reached_arg gap handling).
- motion engine: the game carries both legacy Animation and playable-graph
  paths; decode open (queue). ship legacy Animation clip playback now —
  the timeline's own consumer pattern — and revisit when the decode lands.

## camera

- key schemas (gallop_full_schema.json): CameraPositionData (setType,
  position, charaPos, bezierPoints, charaRelativeBase, charaRelativeParts,
  traceSpeed, nearClip, farClip, cullingLayer, BgColor*),
  CameraLookAtData (lookAtType, position, lookAtCharaPos,
  lookAtCharaParts, ...), CameraFovData (fovType, fov), CameraRollData
  (degree), CameraSwitcherData (cameraIndex).
- types are {0=Direct, 1=Character} (UPDATE 28 census proof); fovType
  only ever 0 in data.
- charaRelativeParts (SETTLED, out/chara_relative_parts_decoded.md): the
  21-member table. 6=InitFaceHeight (face bone at char init — the dominant
  authored target), 11=ConstFaceHeight (cached constant, the Const* family
  11-14 skips bone reads), 15=Position, 16=PositionWithoutOffset, 20=Max
  sentinel. implement GetCharacterWorldPos per the doc: per-character
  accumulation, group averaging.
- chain: CameraPos -> LookAt -> Fov -> Roll -> Switcher. implement those
  five; CameraMotion/HandShake/MotionCamera keys are empty on 15 songs and
  their consumers are not decoded — follow-up.
- locators: 23 CamPos + 2 CamLookAt components per cutt bundle (son1001 is
  the lone legacy rig without them — special-case from the census).

## formation + mic

- formationOffsetSet: 61-field keys (Position, RotationY, LocalRotationY,
  ScaleFactor, visible, warmUpCySpring, CySpringRate, DressIndex, emissive/
  dirt fields stay unwired — stage-light scope). the position track is the
  phase 2 consumer.
- mic (decoded, implement from out/mic_system_decoded.md +
  mic_blend_and_nodes_decoded.md + mic_rate_and_thresholds_decoded.md):
  IKSystem=4 = MicStand keys; hands IK to Mic_Node_L/R; two-threshold
  hysteresis High/Low with per-character rate
  clamp((BodyScale-0.827)/0.338, 0, 1.6665).
- handheld mic prop: propsSettings attachJointNames Mic_Attach_00/_loc;
  prefab `3d/chara/{prop|toonprop|richprop}/prop{major}_{minor:02d}` per
  song (30/61 songs author props, every referenced bundle on disk).
  stand mics are stage dressing, not chara props.

## facial + shading

- facial1Set: face/mouth/eye/eyebrow/eyeTrack/ear/effect/tailMotion keys.
  facialId blend consumer (AlterUpdateFacialNew) is decode-queue — until it
  lands, parse + store, hold base face pose.
- base character shading (NOT postfx, ships here):
  lightmap_defaults_decoded.md — ModulateColor = dc*2*density = WHITE,
  DensityAddColor = BLACK, toon/rim white, outlineWidth 1.0, offset 0.
  publish set per stage_light_publishes_decoded.md.
- LOD (decoded): SetLODShader distance gate, _GlobalCameraFov =
  min(fov/30, 1), per-chara SetOutlineWidthForPower = power*0.325.

## audio (moved here from phase 3 — a silent concert is not a concert)

- oke pairs: `sound/l/{song}/snd_bgm_live_{song}_oke_01/.02` (2 per song)
- vocal mixes: `snd_bgm_live_{song}_chara_{charaId}_01.acb/.awb` per allowed
  singer (1001: 67 chara, 1004: 31, 1093: 63)
- decode chain: ACB header + AWB payload, HCA inside (criware parser
  family; the explorer's parser is the orientation reference). our own
  parser, from-scratch, MIT-clean — no criware sdk, no fork code.
- the clock: once audio plays, timeline time advances from the audio
  source (audio-clocked), not wall clock. keep the clock abstraction; the
  free-run impl stays for silent bench mode only.

## out of scope (later phases)

all PostEffect_* systems; stage light show (phase 3); fade/bgColor;
multi-camera composite; audience per-song keys (stage crowd renders).

## module layout

```
assets/Scripts/
  live/
    timeline/ worksheet_reader.cs, timeline_clock.cs (clock abstraction,
               free-run + audio-clocked impls), key_eval.cs
    motion/  motion_catalog.cs, motion_player.cs
    camera/  camera_director.cs, camera_locators.cs
    formation/ formation_driver.cs, mic_ik.cs
    chara/  chara_shading.cs, facial_driver.cs (gated)
    stage/  stage_loader.cs, stage_missing_notice.cs
    audio/  acb_reader.cs, hca_decode.cs, live_audio.cs
```

## acceptance (all must pass, machine-checkable where possible)

1. worksheet read: a headless run over ALL 61 songs loads every song's
   worksheet with zero sidecar jsons present; the run writes a per-song
   result table (loaded, stage ok, clips resolved count) to a log artifact.
   any song failing = phase open.
2. no shortcut grep: `grep -r "stage_geo_\|timeline/.*json" Assets/` finds
   no shipped repack blobs or pre-extracted worksheets; runtime reads the
   install.
3. motion proof: for son1004 the clip set {1st,2nd,3rd,l,r} resolves
   case-insensitively and all five bind; the slot map length == member
   count for all 61 songs (asserted in the same headless run).
4. camera proof: sampled-frame test — at 5 authored frames the camera
   position + lookAt targets match the worksheet keys (direct types), and
   a charaRelativeParts=6 key tracks the character's face height. artifact
   = the comparison table.
5. mic + shading: a mic-authored song engages IK only on IKSystem=4 keys
   (assert); lightmap publish set applied on one cast (screenshot +
   publish-state dump).
6. audio: oke + vocal mix play for the selected cast on son1004 and the
   dance stays in sync across a 60s window (frame counter vs audio time
   drift < 1 frame).
7. the two missing stages (1157, 1193/3193) show the notice; all other
   songs load their real stage.
8. phase report to the user: the module table above with per-row status +
   the artifacts. "done" without a row's artifact = row not done.

## open questions (unchanged)

facialId blend consumer decode; motion engine playable-graph question;
both queued, neither blocks the rows above.
