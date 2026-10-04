# UV2 phase 3 implementation plan — stage lights + crowd + camera realism

**Status: in progress (10-01 overnight). v1.0.0 shipped; this plan drives the
experimental work.**

## goal

The authored light rig, the crowd, and the remaining camera realism, per
`uv2/phase3.md`'s module table (9 rows) + acceptance gates. Game content
resolves at runtime from the install; v1 = mechanism reference only.

## what the data says (probed this session)

- **globalLight** (row 1): done — the rim block now blends between
  bracketing keys (fdbe0f2); before it published the base key unblended.
- **blinkLight** (row 2): the worksheet carries per-song blink CONTAINERS
  (1048: 12, e.g. `pfb_env_live10117_blinklight_circlelight` 122 keys).
  Each key = per-frame `powerArray` (one power per chara SLOT) +
  `color0Array`/`color1Array` per slot + `LightBlendMode`. The FSM fields
  (pattern/turnOn/keep/turnOff/interval/loopCount) are ZERO on 1048 = the
  key track alone drives power; pattern!=0 songs add the trapezoid stagger.
  The blink GEOMETRY lives inside the stage controller bundle's
  `_stageObjects` hierarchy; the blink MATERIALS ship separately
  (`sourceresources/.../materials/mtl_env_live10117_blinklight000`).
  Resolution = v1's shape: map every instantiated stage child's name →
  gameObject; the blink driver matches the worksheet's root name against
  that map. NO separate prefab loads.
- **cameraMotion** (row 9 note): all dumped sheets author IsEnable=0 with
  no clip PPtrs — not the camera driver. The curve fix (a9e06c8) landed the
  realistic in-segment easing.
- **camera roll** (row 9): camera_roll binds + lerps already; verify on a
  song that authors rolls.

## work order (per phase3.md rows)

1. ~~globalLight publish~~ DONE (fdbe0f2)
2. blink driver: stage-name map + per-frame key-track sample
   (bracket+lerp powerArray/color0Array) + the trapezoid FSM for
   pattern!=0 + power*color publish to the container's renderers
3. spotlight3d (position += characterPosition anchor, targetCameraType)
4. laser (LaserBlink 0..6, blinkPeroid seconds)
5. footlight (LATE pass, 20-slot loop)
6. volumeLight + uvScroll + wash + additional
7. light object resolution: unresolved names VISIBLE flags
8. crowd: audience objects animate, audienceList keys, cyalume, mobControl
9. camera realism: verify roll keys on a roll song; motion-clip camera path
   stays out (data says inactive)

## acceptance

Per `uv2/phase3.md` acceptance section (census run over 61 songs,
sampled-frame FSM proof, laser/footlight/spotlight visible, cyalume 33/33,
audience animate on 1001, no post-effect code, phase report).

## verification

One fresh gate run per landing (bake + player boot + trace grep), CI green
+ exe verified before reporting done. User's exe = the visual gate.
