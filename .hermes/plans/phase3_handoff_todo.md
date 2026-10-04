# UV2 phase 3 handoff — todo list for the new session

**Written 10-02 at the user's ask (he's switching me to a new session).
HEAD on experimental: d57d5b1 (stage_loader + props staged but UNCOMMITTED
files: the props_system.cs trio + stage_loader edits are STAGED, the gate
was running when this was written).**

## state at handoff

- CI green at tip; every landing exe-verified through 6fee5a2 (the camera
  layer-band height fix). The 51-song gate re-run PASSED (51/51 open, 0
  failed-or-missed, 48/51 music — row 3 CLOSED on the fixed build).
- Facial hold + per-chara vocals MOVED TO PHASE 4 (040aad7, user: 'i dont
  care about that much').
- The audit's confirmed-correct rows: global light, blinklight, spotlight,
  laser (+ fixtures), footlight line, light resolution, crowd.

## the todo list (in order)

1. **PROPS (in flight, needs finishing)**: the props_system.cs trio
   (Assets/Scripts/live/props/) + stage_loader wiring is STAGED UNCOMMITTED
   — the child's interrupted work, absorbed. The system: propsDataGroup
   resolution (chara props by major/minor id, stage dressing by common
   bundle code), mic rig spawn (Mic_Attach_00/_loc/Mic_Node_L/R under
   Position), the stand-mic hand IK (two-bone solve, the decoded height
   band + hysteresis), per-frame update. TO DO:
   - DONE 10-03: the stale-trap check first — the 23:35 trace was a
     19:37 run on the PRE-props binary; the props code had never run.
   - DONE: benched on 1004 (the real prop-authored song; 1001 authors
     zero props — the handoff's example was wrong): 3 stage props
     planted, 3 mic rigs, mic ik engaged slots 1-3 both hands at weight
     0.51 (the decode doc's 160cm worked example).
   - DONE: the stand-origin question — the stand prefab pivots MID-POLE
     (bounds y span -0.750..+0.850 at chara root 0); v1's plant lifts the
     root by its bounds bottom. Implemented: lift 0.750 -> base lands at
     y 0.000 exactly (trace: '[0.000..1.600] vs chara root y 0.000').
   - DONE: committed cc29d10 + pushed + CI + exe verify.
2. **Shading row's per-chara outline LOD tail**: v1's model controller sets
   _OutlineWidth = outlineWidthPower * 0.325 per chara (from the camera
   pos key's per-frame updateInfo / the bgColor1 track carries Saturation +
   outlineWidthPower). 1048's CharaParts keys carry EMPTY
   CharaPartsDataArray (authored off) — check whether ANY song authors
   them; if none, note it zero-impact and close the row as MOSTLY with the
   finding; if some do, wire the read.
3. **Phase-2 gate re-run artifact**: DONE this session (Logs/all_songs_run.json
   re-ran 10-02: 51/51 open, 0 failed-or-missed, 48/51 music) — the
   phase-2 doc's acceptance evidence line still cites the 09-30 run; refresh
   it to cite the 10-02 rerun. (The row-3 table is already refreshed, d57d5b1.)
4. **Laser fixture binding on the CURRENT build**: the laser fixtures
   instantiate 1 (the loose object) but the containers bind 0 unresolved =
   the worksheet names ('pfb_env_live10117_laser000 - 0') vs the
   instantiated piece's name (go.name = 'pfb_env_live10117_laser000', no
   ' - N' suffix). The trace at handoff showed 0 unresolved (the bind
   matches the bare name?) — VERIFY on 10117 that the 23 entries bind;
   if they bind 0/23, fix the name matching (the clone-suffix strip).
5. **After props + the above**: phase 3 is complete. The next phase per
   the user's ordering = phase 4 (post effects + the moved facial/vocal
   rows), GATED on the facialId decode + a vocal decode check.

## the traps that bit this session (do not repeat)

- delegate_task tasks = ONE clean JSON array (a mangled call exploded into
  15/23 fragments twice this session)
- git status must come back empty of source files after every commit
  (shipped without spot_lights.cs once — CI failed on the missing type)
- yield in a try/catch body = CS1626 (the fixture instantiation coroutine
  lived inside run_phase_stage_bundle's try block once)
- sleep-polling children = waste (the async handoff delivers results
  between turns; the user corrected this hard)
- instantiate_spotlight_fixtures resolves via the controller's AssetHolder
  table (spotlight3dNNN -> prefab PPtr binds at deserialization), NOT
  LoadAsset-by-name (the bundle exposes 1 asset) and NOT LoadAllAssets
  (only the controller comes back)
- the laser fixtures = StageController._laserObjects LOOSE GameObjects
  (Instantiate(stagePrefab) never brings loose objects along); the
  worksheet's entries name runtime clones ('{fixture} - {index}') — one
  clone per distinct index, named like the entries, fixes the 0/23 bind
- a trace's mtime dates the RUN, not the build: the 23:35 'stale props'
  trace was a 19:37 run on the pre-props binary — check the mtime against
  the binary's before inferring the code never ran
- git add returns non-zero on an ignored-dir warning even when it STAGED
  the tracked files — the && commit never ran; commit separately
- the 'experimental' TAG reappears from old clones of the session state;
  check git show-ref before every push (src refspec matches more than one)
- the stand prop prefab pivots MID-POLE (bounds span both sides of zero) —
  the plant lifts the root by its bounds bottom or the stand sinks 0.75m

## decode docs this phase (canonical, ~/umadump/out/)

uv2_generic_body_texture_decode.md, uv2_camera_freeze_lookat_decode.md,
uv2_camera_curve_decode.md, uv2_camera_roll_verify.md,
uv2_spotlight_laser_decode.md, uv2_spotlight_fixture_decode.md,
uv2_camera_layer_height_decode.md (the height-rate band mechanism).
