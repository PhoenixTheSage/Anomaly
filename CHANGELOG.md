# Changelog

Anomaly uses semantic versioning. The project began at `1.0.0`; the lineage below is reconstructed from repository history so the current version reflects shipped capability rather than the inherited SE-DLSS version.

## Unreleased

## 1.9.0 — 2026-09-18

- Shared atmospheric volumes (opt-in): medium registry, inject/integrate/composite shaders, interior upload, directional volume shadows, GPU timers, and pack docs (`Docs/SharedVolumetrics.md`). Final Frontier Atmosphere and Volumetric Clouds consume this path.
- Slice AN shipped: catalog `volumeSunShadow` (PublishOnly `output`, not reserved). Helper `AnomalyVolumeSunShadow` fail closed to 1 when `.a < 0.5` (unbound SRV samples 0). AfterAtmosphere IsolatedSub dest uses `AnomalyIsolatedSubEnergy`. Extras stay **320 B**. Do not bind Keen CSM. Volumetric Clouds stamps weather OD along `AnomalySunToward`, then IsolatedSub dest, then IsolatedMix.
- Celestial background registry and main/probe shader contract for Final Frontier: live uniforms, immutable float4 catalogue data, paired compilation, depth masking, and explicit fallback (no foreground-lighting fork). MSAA falls back to vanilla. Solar glare / projected labels remain deferred.
- Point-shadow atlas: isolate character-shadow projection from Keen pass constants; capture cube-face geometry relative to the light so camera motion does not rewrite transforms; preserve camera-relative `playerDepth`. Contact-shadow helpers and projection acceptance scripts land with the atlas work.
- Folder settings pages unregister by their full key without treating pack Settings pages as reserved host pages.

## 1.8.0 — 2026-09-17

- Slice AL shipped: `MeshDepth` VS skins with Keen `VertexTemplateBase` and interpolates camera-rel world (euclidean `length(world-LightPos)`). Do not bind Keen DEPTH_ONLY VS (`z=max(z,0)` striped the atlas). IsolatedSub mins `CubeVisibility` when `RequestPointShadows > 0`. Contact still owns GBuffer hits — do not skip `TryGetCamRelBox`. First-person skips `SkipInMainView` + head/hood/glass/visor (no Harmony `EnableHead`). AABB cubes stay optional (`RequestWorldBoxes`). Extras stay **320 B**.
- Slice AM terminator: `AnomalySunTransmittance` is a monotonic squared limb (`smoothstep(-t, t, μ)` then `geo²`). Returning raw `geo` when `μ≤0` and `geo*exp(-OD)` when `μ>0` peaked vis on the night side of `μ=0` (bright band) and cut IsolatedMix HDR into a wall. Cap is 0.40 (0.18 sat inside Pertam's geometric sunset). `hdrLift` stays pack-side, deep-day only. Night fill remains AJ × 0.05. Extras stay **320 B**.
- Slice AM night fill is AJ × `AnomalyVolumeNightScale()`, not dest luma. Dest luma vanished over night terrain. 3-arg `destRgb` overload is ignored. Extras stay **320 B**.
- Slice AM shipped: `AnomalyVolumeNight` night inscatter (AJ × 0.05, Keen night ambient from orbit). AJ `AnomalyVolumeAmbient` stays in-cloud day fill — 2.8% sun over dest≈0 is headlights. `AnomalySunTransmittance` is air-limb twilight `sqrt(2h/r)` plus 6-step air-column OD (no hard `μ≤0`, no 12% Lambert wrap). Extras stay **320 B**. Volumetric Clouds rebases; Aurora `NightAt` uses transmittance. Do not bind Keen CSM.
- `Label(text, get)` now pulls on `Refresh()` (mount used to freeze the first getter result). Still no `CustomValueGetter`. Packs that show live km / planet status should call `Refresh()` from `Plugin.Update` when the string changes.
- `AnomalyLightingViewPosUnjittered` / `AnomalyViewToUnjitteredUv` / `AnomalyLightingJitterUv` / `AnomalyViewToDepthUv`: contact / SSGI reconstruct unjittered view (lights are unjittered) and sample `linearDepth` at unjittered UV plus Halton (`AnomalyLightingJitter` is Projection M31/M32). `AnomalyViewToLightingUv` stays the inverse of live `compute_screen_ray` for LightPoint BRDF. Do not sample jittered depth at unjittered UV, and do not project with `AnomalyUnjitteredViewProj` as a substitute. `AnomalyIsolatedSubEnergy` clamps `removed` to dest and applies a soft knee so occ=1 at the photometric falloff cannot dest-punch (duplicate silhouettes that do not appear in the bright center). 3-arg overload also clamps to unshadowed point-light energy.
- `AnomalyViewToLightingUv(viewPos)` is the inverse of `compute_screen_ray` / `AnomalyLightingViewPos` (live Keen projection, including Halton M31/M32). Contact / SSGI marches sample `linearDepth` at that UV. Do not project with `AnomalyUnjitteredViewProj` against jittered depth (shadows swim when the camera moves). Do not scale contact **step length** with `AnomalySafetyScale` — dropping 48→8 steps during a look changes origin bias and the umbra jumps. `AnomalyMarchSteps` is for volume sample count, not AfterLighting contact.
- `AnomalyIgnWorld(world.xz)` is interleaved-gradient dither in world XZ so contact / SSGI step phase does not crawl when the camera moves. Do not scale the seed (high-frequency IGN flips when viewPos jitters 1 cm). Pixel `AnomalyIgn` still follows the raster (volumes). IsolatedSub of tiled lights marches the brightest N by photometric energy; vis may continue ~1.4× `light.range`, IsolatedSub energy does not (a BRDF tail dest-punches at the falloff). Adjacent-floor self-hits (`receiverZ - sceneZ` 8–15 cm) IsolatedSub dest to black — use a ~0.22 m min sep and a fixed along-L bias, not a camera-ray radial cone.
- Slice AK shipped: extras CB is **320 B**. `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil` are radii from the planet center (meters), fail closed to 0. Game-thread `PlanetAtmosphere` publishes the nearest `HasAtmosphere` / CloudLayers planet; `FrameTemporal` copies onto both extras write paths. HLSL `AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade`. Do not Harmony `MyAtmosphereRenderer` or skip `Atmosphere_sphere`. Visual ceil equals air top today (`AverageRadius + AtmosphereAltitude`). Volumetric Clouds rebases onto extras; Base height 0–1 from terrain, CloudLayer height bands, GBuffer-only march clip.

## 1.7.0 — 2026-09-16

- Slice AJ night fill: `AnomalySkyAmbient` is unlifted `SunColorRaw * 0.028` only. Do not scale by `AmbientForwardPass` (Keen adds probe ambient into that field). `AnomalyVolumeAmbient()` caps at the same 3% so a stale extras tail cannot equal sun. `AnomalySunVisibility` wrap is atmosphere thickness, **capped at 12% of occluder radius**, so geometric AtmosphereRadius cannot sun-light the night disk.
- Slice AI shipped: extras CB grew with `AnomalySunColor` / `AnomalySunDiffuse` / `AnomalySunToward` / `AnomalySkyLuma`; `AnomalyMarchSteps` cheapens fullscreen marches from a uniform camera-to-volume distance; `AnomalySunVisibility` uses local-up × sun with atmosphere-thickness wrap. IsolatedMix stays over — `src.rgb` is LBuffer energy. No IsolatedMixLit.
- Documented the own-the-gap rule so pack lighting/LOD hacks become Anomaly work instead of private contracts (`wiki/Framework-gaps.md`).
- Live Replace is exclusive at draw time: a pack-disabled Replace no longer permanently disables IsolatedAdd/Sub siblings on the same slot. Two live Replaces still fail closed.
- IsolatedSub dest-write is Anomaly-owned: pack writes a 0–1 dest fraction (`AnomalyIsolatedSub` / `AnomalyIsolatedSubEnergy`). IsolatedSub draws occupancy to scratch, then **blends** `dest*(1-src)` onto dest (same dest RTV as Replace). IsolatedSub blits dest for t0 when dest aliases LBuffer so occ can be `removed/dest`. Umbra stamps IsolatedSub `.a` for Reactive. `TryGetProgramStatus` reports last merge/skip/dest.
- `AnomalyIgn` is interleaved-gradient dither in pixel coordinates. `AnomalyIsolatedSubEnergy(removed, dest)` is the IsolatedSub fraction so a minVis OR of many lights cannot zero ambient.
- Json `passes[]` with different ids may share one `Fullscreen/` hlsl (IsolatedSub + debug Replace). Matching existing programs by file used to overwrite the first pass.
- Fullscreen compile failures stay on Status as `id!compile`. Packs call `TryGetProgramStatus`. Keen `Math.hlsli` already defines `rand` — fullscreen shaders must not redeclare it.
- `AnomalySceneUvOffset` converts a full-res pixel radius to UV (`AnomalyInvSceneSize`). Scaled passes must not march with `AnomalyInvPassSize`.
- `FullscreenPassRegistry.DrawFullscreen(rc, width, height)` wraps Keen `DrawFullscreenQuad` with the RT viewport. A no-viewport call uses `SetScreenViewport()` and clips scaled AfterFullscreen RTs to the top-left of UV 0–1.
- Optional `__compute_shader` + `AnomalyComputeDest` u0 (`ANOMALY_FULLSCREEN_COMPUTE`) so Display Replace can grade into a UAV when dest aliases t0 after upscale.
- Isolated programs with `ContributeVelocity` reconstruct camera MVs at Isolated.a hit distance (`IsolatedVelocity.hlsl`) so DLSS can lock volume curtains instead of far-plane sky parallax.
- Catalog `pointLights` / `tileIndices` wrap Keen tiled point-light GPU buffers (`PointLightCatalog`). Optional AABB light-view atlas (`PointShadowPass`, `BoxDepth.hlsl`) and 64³ occupancy clipmap (`OccupancyStamp` / `OccupancySplat`).
- Pack 3D SRVs: `#define ANOMALY_PACK_SRVn_TYPE Texture3D` before `AnomalyFullscreen.hlsli`. AfterLighting helpers reconstruct LightPoint UV/view/normal (`AnomalyLightingUv` / `AnomalyLightingViewPos` / `AnomalyLightingN`).
- Rich HUD `Refresh()` now pushes dropdown selections from getters (same pull path as sliders/checkboxes; still no `CustomValueGetter`). Ships `HudOverlayRegistry` for corner status.

## 1.6.0 — 2026-09-10

- Packs and consumers register Rich HUD pages under **Anomaly Shaders** via `TerminalConfigRegistry` (no second Rich HUD client; Master optional).
- `HudOverlayRegistry` draws optional corner status (`Register(id, get)`) when Master is registered. Getters return a cached string; they must not write config.
- AfterUpscale Display tenants get an fp16 dest (`DisplayDest`) so BT.2390 can write scRGB above 1; `TonemapInputs` captures bloom / luminance / dirt without packs patching `MyToneMapping.Run`.
- Config saves are dirty-flagged and flushed off the HUD thread after ~400 ms so slider ticks cannot stall Present.
- Parallel shader-cache fill, program warmup, deferred-context guards, and a main-view gate keep LCD / TargetView / TargetCamera off HDR-slot work.
- Isolated fullscreen programs can stamp a dilated `reactiveMask` for DLSS; owned-pass phases and render-trace bind help packs debug the frame.
- Fullscreen HLSL lint plus wiki/docs for terminal config, Display dest, and pack onboarding.

## 1.5.0 — 2026-09-06

- Publishes visually proven object and camera motion vectors in current-to-previous pixel-space convention.
- Adds persistent motion visualization and a dedicated Velocity Debug page.
- Separates concise framework and velocity status screens.
- Keeps probes, checkpoint copies, and the full-resolution pipeline-audit target opt-in.
- Documents the consumer contract and DLSS integration requirements.

## Semantic lineage

- `1.0` — Pulsar shader-framework foundation and initial pack model.
- `1.1` — compile interception, camera velocity, ActorID history, and GBuffer object velocity.
- `1.2` — programmatic pack registration, rollback validation, and named overlays.
- `1.3` — additive injection, pack defines, GBuffer attachments, lighting binds, named buffers, and expanded stages.
- `1.4` — owned render slots, data-driven fullscreen programs, depth/history catalogs, and temporal participation.
- `1.5` — corrected and visually proven motion-vector output, audit instrumentation, persistence, compatibility guidance, and production-safe debug gating.
- `1.6` — terminal config registry, Display dest / tonemap inputs, deferred config I/O, compile warmup, reactive stamp, and pack-facing render trace.
- `1.7` — extras-CB sun/sky/march helpers, IsolatedSub dest-write, compute Display grade, point-light catalog, occupancy/light-view owned passes, IsolatedVelocity, and scaled-pass UV/viewport.
- `1.8` — MeshDepth atlas skinning, sun transmittance terminator, planet atmosphere ceilings, and lighting UV/jitter helpers.
- `1.9` — shared atmospheric volumes, celestial background registry, volumeSunShadow catalog, and camera-stable point-shadow atlas.

The intermediate entries describe compatibility epochs, not previously tagged releases.
