# Shared atmospheric volumes — implementation status

**2026-09-18: integrated, opt-in acceptance build. Not release validated.**

Anomaly owns the shared renderer. Final Frontier supplies atmospheric fog and Clouds
supplies its existing weather/shape/detail density through an adapter. Enable it in
Final Frontier → Atmosphere; it defaults off until the acceptance scenes pass.
Existing star/sun settings and Clouds presets are preserved. Dramatic is the default
fog style; Clear and Heavy Fantasy are also available.

## Implemented path

- `VolumetricMediumRegistry` owns copied parameters, planet-relative origins, bounds,
  motion and optional legacy interval callbacks. The renderer freezes provider membership
  for each frame; simulation updates cannot revoke an already-recorded near interval.
- Shared shader compilation supports up to four providers, each with 16 float4 uniforms
  and three catalog textures. Coefficients are summed before Beer–Lambert integration.
  High uses 8-pixel columns, 96 logarithmic slices and up to 8 km.
- Three stabilized 2048² sun depth maps cover 128 m, 1 km and 8 km receiver regions.
  Independent serial mesh submission precedes Keen's scheduler initialization; projection
  view IDs are reused only before the normal batch. Actual terrain/grid/character/cutout
  caster completeness and engine LOD interactions still require live verification.
- Spatial light optical depth evaluates all participating media. Planetary body occultation
  and geometry shadows gate direct sun. Floating-point injection/history/integration feeds
  depth-aware reconstruction, near-surface refinement and one linear HDR `L + T * scene`
  composite after atmosphere and before transparency.
- Airtight room cells are captured on the simulation thread regardless of oxygen amount.
  Sorted cells and bounds are cached by topology; unchanged frames update only grid transforms
  in a reusable discard-written buffer. Deferred draws retain their original buffer contents.
  Membership, cell-array identity or generation changes rebuild the immutable cell buffer. Missing/stale
  data reports degraded exclusion. Membership/topology changes reject volume history.
- Clouds retain their renderer outside the shared distance. A successful frame transaction
  clips its near interval; failure restores the full legacy interval. Its surface-facing
  `volumeSunShadow` product remains published, while the legacy whole-scene attenuation
  stamp is disabled during joint rendering to avoid duplicate attenuation.
- Before granting the near interval, the host checkpoints scene color, reactive state, velocity
  and writable fullscreen history textures. If the shared composite fails after legacy rendering,
  it restores those resources, resets every frozen/current interval callback and replays the
  AfterAtmosphere fullscreen programs once with the full legacy interval. Successful frames retain
  their existing order. The GPU copies add memory and bandwidth cost that still needs live timing;
  copy failure or device loss cannot guarantee a recovered image, and device loss is rethrown.
- Fog uses planet height, world-anchored advected noise, boundary/distance fades and
  anisotropic sun scattering in Anomaly lighting units. Density, height, variation, wind,
  tint, phase, contrast, distance, quality and debug controls have their own Atmosphere page.
- Reprojection accounts for camera/provider motion, density/visibility/disocclusion changes,
  camera cuts, planet transitions and recreation. Current reactive coverage joins Anomaly's
  mask already consumed by FSR/DLSS. Reliable opaque-volume motion is contributed before
  upscaling; mixed layers use reactive coverage. FG consumes current volume confidence and
  falls back toward the current frame while preserving its float16 color intermediates.
- Nonblocking GPU timestamps separate geometry shadows, spatial light transport, injection,
  integration and reconstruction. MSAA, missing resources and failed initialization fall
  back to legacy rendering. The approximately 6 ms target has not been measured.

## Draft v1 products and lifecycle

The catalog exposes `volumeScatteringTransmittance`, `volumeRepresentativeDepth`,
`volumeMotion`, `volumeReactive` and `volumeSunVisibility`. Wrappers expose
`ContractVersion`, `Frame` and `ResourceEpoch`; availability requires the exact current
frame and resource epoch. Consumers must check availability, never treat stale/missing
resources as valid shadows, and never retain borrowed views across recreation.

`SetIntervalCallback` is optional for adapters replacing a legacy renderer. A zero
interval restores legacy rendering; a positive interval reserves only the committed
near distance. Unmigrated effects remain independent and are not physically integrated.
The ABI is provisional until acceptance. Provider source/binding registration is immutable;
uniforms/origin/bounds/motion are copied. Artistic changes should call `InvalidateHistory`.

## Automated validation

All four plugins build for their configured frameworks. Anomaly retains existing CS0162
warnings in ShaderCompileIntercept. No deployment is performed by these build commands.

```powershell
dotnet run --project Tests/Volumetrics/Volumetrics.csproj -c Release -- Assets/Shaders '../FinalFrontier/Assets/Celestial/Atmosphere/Fog.hlsli' '../VolumetricClouds/Pack/Medium/Clouds.hlsli'
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never -p:UseSharedCompilation=false
```

113 checks cover frame ownership, copied parameters, resource recreation, analytic/zero/
overlapping fog, actual HLSL execution on D3D11 WARP, production volume integration,
transformed room lookup, synthetic shadow maps and planetary occultation. Production
light/injection/reconstruction kernels compile for fog only, clouds only and both provider
orders. The production injection, integration and reconstruction functions also execute on WARP
with synthetic media: bounded history, disocclusion/density rejection, blocked-light
rejection, invalid provider values, scene-depth clipping, finite camera-turn motion and
zero-density composition. The spatial light kernel tracks a moving density boundary.
Frame settings are immutable, and the shadow-bias test includes a receiver just behind
a blocker. These checks do not establish real scene geometry or performance.

**2026-09-20 compile fix:** Joint fog+clouds failed in-game (`AnomalyLinearSampler`
undeclared in CloudOctree under volume kernels). `AnomalyVolumeCommon.hlsli` aliases
it to `VolumeLinearClamp`. WARP fog+clouds passes 113 after the alias.

**2026-09-20 CB slot fix:** Enable then failed at Bind (`IndexOutOfRangeException`) —
Keen CB slots are 0–7 only; shadow constants moved from `b8` to `b5`.
SE-FG additionally passes 12 HDR/SDR gradient/star checks plus eight reactive-mask checks.
The color fixture explicitly seeds its no-HUD copy, matching production behavior.

## Remaining acceptance and tuning

Cloud-only appearance comparison, real off-screen/cutout casters, moving-room/breach
behavior, complete temporal stability, device loss during recording and cloud-gap shaft
motion need in-game evidence. Spatial light-grid resolution and full-resolution refinement
are initial budgets and may need substantial tuning on the GTX 1070 Ti. Cloud scattering
uses the shared physical lighting units; visual equivalence to legacy artistic lighting is
not assumed. Reduce sampling/volume resolution before reducing occluder correctness.

For clouds-only testing enable Atmosphere and set fog density to zero; for fog-only disable
Clouds; then compare combined mode. Record per-pass GPU milliseconds at 1080p along with
native/FSR/DLSS/FG mode and the chosen quality. Do not ship until the checklist passes.

## In-game acceptance checklist

- [ ] Broken clouds over a valley at low sun; moving gaps move shafts.
- [ ] Grid, mountain and off-screen terrain blockers; actual cutout silhouettes.
- [ ] Sealed cockpit including zero oxygen, open hangar, newly opened breach.
- [ ] Moving/rotating grids and changing room topology.
- [ ] Crossing cloud base and atmospheric boundary; sunrise, sunset, planetary night.
- [ ] Stationary/moving camera under native, FSR, DLSS and FG.
- [ ] Clouds-only, fog-only and combined modes with per-pass GPU measurements.
- [ ] Extreme exposure/HDR gradients; resize/device recreation/missing providers/MSAA fallback.

Keen molecular atmosphere is retained. The joint cloud/fog integration still
composes approximately with that existing scattering. V1 excludes local-light shafts,
fluid simulation, coloured glass shadows and molecular-atmosphere replacement.

## Local acceptance launch and evidence

The verified Interim Pulsar source configuration enables Anomaly, VolumetricClouds,
FinalFrontier and SE-FG directly from `T:\Cursor Projects`. Its asset manifests point
at the source assets. Restart the usual Pulsar launch to rebuild changed sources;
no copy into the older Local plugin folders is needed. Atmosphere remains opt-in.

First enable Final Frontier → Atmosphere in a planet atmosphere with High quality.
Start with Clouds enabled and Fog density zero to compare cloud-only appearance, then
restore Dramatic for combined mode. Check geometric visibility and interior-mask debug
views before evaluating artistic quality. A degraded status is a failed interior guarantee,
not a visual pass. Repeat native/FSR/DLSS with FG off and on at 1080p.

While active, the game log records `Anomaly volume sample:` on activation and every
10 seconds, with render size, provider count, quality, distance, interior status and
recent GPU timings. These asynchronous samples are diagnostic, not synchronized per-frame
benchmark data. Record a separate game log for each scene/mode, then collect it with:

```powershell
./scripts/CollectVolumeAcceptance.ps1 -Scene 'Valley low sun combined' -RenderingMode 'FSR+FG' -OutputPath './artifacts/valley-fsr-fg.json'
```

Use `-LogPath` for an older log. The collector refuses empty results and retains pending
timings as null. Labels identify the operator's scene/mode; they are not detected proof.
The in-game checklist above is still unpassed. No 6 ms performance claim is made.
