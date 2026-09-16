# Changelog

Anomaly uses semantic versioning. The project began at `1.0.0`; the lineage below is reconstructed from repository history so the current version reflects shipped capability rather than the inherited SE-DLSS version.

## Unreleased

## 1.7.0 — 2026-09-16

- Slice AJ night fill: `AnomalySkyAmbient` is unlifted `SunColorRaw * 0.028` only. Do not scale by `AmbientForwardPass` (Keen adds probe ambient into that field). `AnomalyVolumeAmbient()` caps at the same 3% so a stale extras tail cannot equal sun. `AnomalySunVisibility` wrap is atmosphere thickness, **capped at 12% of occluder radius**, so geometric AtmosphereRadius cannot sun-light the night disk.
- Slice AI shipped: extras CB grew with `AnomalySunColor` / `AnomalySunDiffuse` / `AnomalySunToward` / `AnomalySkyLuma`; `AnomalyMarchSteps` cheapens fullscreen marches from a uniform camera-to-volume distance; `AnomalySunVisibility` uses local-up × sun with atmosphere-thickness wrap. IsolatedMix stays over — `src.rgb` is LBuffer energy. No IsolatedMixLit.
- Documented the own-the-gap rule so pack lighting/LOD hacks become Anomaly work instead of private contracts (`wiki/Framework-gaps.md`).
- Live Replace is exclusive at draw time: a pack-disabled Replace no longer permanently disables IsolatedAdd/Sub siblings on the same slot. Two live Replaces still fail closed.
- IsolatedSub dest-write is Anomaly-owned: pack writes a 0–1 dest fraction (`AnomalyIsolatedSub`). IsolatedSub draws occupancy to scratch, then **blends** `dest*(1-src)` onto dest (same dest RTV as Replace). IsolatedSub does not blit dest for t0. destHistory Dest[p] merge never reached Present. Umbra stamps IsolatedSub `.a` for Reactive. `TryGetProgramStatus` reports last merge/skip/dest.
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

The intermediate entries describe compatibility epochs, not previously tagged releases.
