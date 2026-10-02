# phase 4 — post processing (the game's render look)

status: drafted from decoded docs, restructured 09-28 with hard acceptance
gates. build after phase 3 closes. user-gated: phase 3 ends at an un-graded
frame by design; this phase turns it into the game's picture.
end goal context: 1:1 live concert — the post chain IS the game look.

## how to build this (umaviewer v1 inspiration, structure only — no code copied)

- umamusume/Gallop/RenderPipeline/ShaderManager.cs (2001 L) — how v1
  publishes shader globals per camera and orders the post chain.
- umamusume/Gallop/ImageEffect/* — the per-effect component shape v1 uses
  (bloom/dof/film/fog as small CommandBuffer-blit components).
- umamusume/Gallop/Live/Cutt/UpdateInfo/PostEffectUpdateInfo_* — the
  consumer structs that carry worksheet fields into the effects; our
  key types mirror this handoff.


## module table — the phase closes when every row is REAL

| # | module | status | gate |
|---|---|---|---|
| 1 | bloom + diffusion (bloomIntensity + Min(diffusionHint*0.05, 1.5); NEVER emission multiply) | decoded | sampled-frame bloom == authored + capped diffusion |
| 2 | dof (dispatch 1-based: 1=DofBloom, 3=PostDiffusionBloom_Rich no-dof, 5=pure Dof; signed coc, cb0[140] farBlend, 9-tap downsample) | decoded | dispatch matches song config; coc sign correct at far side |
| 3 | film 1/2/3 (10-keyword filmMode table, _DimmerColor white, NO power scaling) | decoded | composites without power scaling; VignetteAdd-inverse look present |
| 4 | global fog (density inversion 1-FogDensity*0.01, FogLength per-component, W>=1e-6, height/radial by flag byte) | decoded | fog inversion visible on a fog song (42 author it) |
| 5 | fade (fadeKeys consumer) | decoded | screen fades run on all 61 songs |
| 6 | facial hold + the facialId blend (MOVED FROM PHASE 2, user 10-02 'i dont care about that much': the stub parses 19 facial fields, nothing consumes them; the facialId blend decode (AlterUpdateFacialNew; 4-digit pattern = motion-set prefix + clip slot, 1001/8001 specials) gates this row) | GATED on the facialId decode | keys parse + hold base pose; the blend matches the game's consumer |
| 7 | per-chara vocal mixes (MOVED FROM PHASE 2, user 10-02: only the oke instrumental plays today; the acb/awb chain ships the shape — the vocal mixing decode at ~/umadump/out/uv2_vocal_mixing_decode.md carries the mixing contract) | GATED on a vocal decode check | a fully-voiced song plays chara vocals in sync with the clock |
| 8 | radialBlur + Fluctuation + Vortex + tiltShift | GATED queue 5 | ships when the decode doc lands |
| 9 | chromaticAberration + LensDistortion | GATED queue 6 | ships when the decode doc lands |
| 10 | colorCorrection + exposure + toneCurve | GATED queue 7 | ships when the decode doc lands |
| 11 | sunshafts | OUT by user bench | no code until the user benches it in |

rows 1-5 are implementable now from the decoded docs. rows 6-8 stay out
of the build until their decode docs exist in ~/umadump/out/ — a guessed
implementation of an undecoded consumer is a shortcut violation, not
progress.

## track population (census, songs of 61, per song)

bloomDiffusion 61, dof 61, film 61 (all three layers), radialBlur 61,
fade 61, tiltShift 60, Fluctuation 53, Vortex 42, colorCorrection 49,
Exposure+ToneCurve 40 (always together), globalFog 42, chromatic 22,
LensDistortion 14. empty = that stage disabled for the song; not
defaults.

## implement now (decoded)

bloom + diffusion (GAME_BLOOM_PIPELINE_DECODED.md,
GAME_POSTBLOOM_COMPOSITE_DECODED.md, out/postbloom_composite_pass0_math.md,
out/draw_helper_globals_publish.md, out/audience_props_bloom_answers.md):
- bloom = bloomIntensity + Min(diffusionHint * 0.05f, 1.5f). never an
  emission multiply (the old viewer's bug).
- composite pass 0: $Globals rows per draw_helper publish;
  _colorBlendFactor (cb1[1].x) is the only UnityPerMaterial output scale.

dof (out/dof_pipeline_decoded.md, out/postdofbloom_coc_downsample_decoded.md):
- dispatch 1-based from song config: 1=DofBloom, 3=PostDiffusionBloom_Rich
  (no-dof), 5=pure Dof.
- focus = camera.InverseTransformPoint(focal).z / farClip; behind-camera->0.
- coc = (1/z - focal) * scale, SIGNED (beyond-focus side blurs; no abs).
- cb0[140] subtrahend = farBlend (authored DofFocalSize + runtime focal);
  scale = InvRT.y * aspect. cb0[138] jitter; 9-tap 1/7 saturated
  downsample, center-tap coc. sharp band clamped by GetMaxForcalSize
  (floored 30); dofSmoothness >= 0.1; coc curve linear pow(1.0, x).

film 1/2/3 (out/postfilm_mode_and_dimmer_decoded.md):
- filmMode keywords: [0]NONE [1]LERP [2]ADD [3]MUL [4]VIGNETTE_LERP
  [5]VIGNETTE_ADD [6]VIGNETTE_MUL [7]MONOCHROME [8]SCREENBLEND
  [9]VIGNETT_SCREENBLEND.
- _DimmerColor = white, runtime-created. NO film-power scaling — authored
  p=1.0; VignetteAdd-inverse +10-13% IS the look.
- BlinkLight* fields on film keys couple to phase 3 blink objects by
  BlinkLightName hash; IsAdjustedBlinkLightColor gate.

global fog (out/global_fog_block_decoded.md, out/globalfog_consumer_decoded.md):
- _Global_MaxDensity = 1.0 - FogDensity*0.01 (inversion); FogLength =
  Max-Min per component, W >= 1e-6.
- consumer blits Gallop/ImageEffects/GlobalFog; pass 1 height / pass 2
  radial by the param's radial flag byte; _SceneFogMode = (mode, flag,0,0).

fade: screen fade from fadeKeys — simple consumer.

chain order (fidelity target, GAME_RUNTIME_ARCHITECTURE.md): per camera
PostEffect_{Bloom, BloomDiffusion, DOF, ChromaticAberration,
ColorCorrection, Exposure, FilmRoll, Flucturation, GlobalFog, Hatching,
LensDistortion, LetterBox, PostFilm, RadialBlur, SunShaft, TiltShift,
ToneCurve, TransmittedLight, Vortex}.

## module layout

```
assets/Scripts/live/postfx/
  post_chain.cs      per-camera pass sequence, game order
  bloom_diffusion.cs dof.cs film_layers.cs global_fog.cs fade.cs
  gated/ radial_blur.cs fluctuation.cs vortex.cs tilt_shift.cs
         chromatic.cs lens_distortion.cs color_correction.cs
         exposure_tonecurve.cs
```

## acceptance (all must pass)

1. sampled-frame math proofs (son1004): bloom value == authored +
   capped diffusion; coc value at two authored depths matches the signed
   formula computed offline; fog density inversion constants correct.
   artifact = the comparison table.
2. dof dispatch: all 61 songs route to the mode their song config names
   (headless assert over the config matrix).
3. film: the keyword table implements all 10 modes; no power scaling
   anywhere in the film path (grep gate: no `pow(` on film inputs beyond
   the decoded coc curve).
4. gated rows absent: grep gate — zero gated-module components in the
   build until each decode doc lands.
5. no sunshafts code.
6. visual sign-off: the user's windows exe only (the linux rig is
   GLES-blind on game shaders) — deliver the exe, wait for the bench,
   no self-declared visual pass.
7. phase report: module table per-row status + artifacts.
