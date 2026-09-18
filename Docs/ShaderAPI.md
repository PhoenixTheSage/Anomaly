# Anomaly Shader API

Celestial background providers now have a dedicated [initial contract](CelestialBackgrounds.md): depth-masked main/probe rendering, copied uniforms/data and paired compilation. MSAA retains vanilla; in-game validation remains pending.

Extensible shader framework for Space Engineers 1. First product is a velocity buffer; the same compile hook must also support **additive injection** into Keen programs and **wholesale replacement** of named programs.

This is the architecture. Implementation order is [ROADMAP.md](ROADMAP.md) (velocity + hook) then [Extensibility.md](Extensibility.md) (generalize beyond motion vectors). Product phases and Keen facts remain in [PLAN.md](PLAN.md). File-level Keen inventory is [KeenShaders.md](KeenShaders.md).

---

## Intent

Two products share one chokepoint:

| Product | Who writes HLSL | Who owns the draw |
|---------|-----------------|-------------------|
| **Buffer API** (velocity now; later TAA / SSR inputs) | Anomaly, injected into Keen | Keen GBuffer, or an Anomaly-owned fullscreen pass |
| **Shader override API** | Another plugin’s overlay files | Still Keen’s pass, but Anomaly-compiled bytecode |

Both need: **intercept Keen’s HLSL compile** (include root, macros, optional source/bytecode swap) and **pass-begin bind** (extra RT/SRV). Without that hook, neither injection nor replace exists.

Default for velocity: **include-inject the shared GBuffer stages**. Do not fork `Materials/Standard/Pixel.hlsl` and friends. See [PLAN.md](PLAN.md) GBuffer piggyback.

---

## Why not an Iris-style full pipeline swap

[Iris](https://github.com/IrisShaders/Iris) (Minecraft) looks like “a folder of GLSL.” Internally it is a **named-stage renderer swap**, not a 1:1 overlay of vanilla programs.

When a pack is loaded, Iris **replaces world rendering** with a small named set (`gbuffers_terrain`, `gbuffers_entities`, `composite`, `final`, `shadow`, …). Missing files **fall back** (`gbuffers_terrain` → `gbuffers_textured` → `gbuffers_basic`). At load it **rewrites pack GLSL** (version, uniforms, `colortexN`). Vanilla Mojang programs are unused for world geometry while a pack is active.

That is cheap because vanilla Minecraft shading is tiny. SE already has a real deferred engine: GBuffer, tiled lights, CSM, HBAO, bloom, OIT, atmosphere, GPU particles ([KeenShaders.md](KeenShaders.md): 215 files). Replacing that the Iris way means **reimplementing Keen’s renderer**. Anomaly must not do that.

Steal from Iris:

1. **Named stages**, not 215 file keys, as the public API.
2. **Preprocessor rewrite at compile** (defines + includes), not a maintained fork of Keen’s tree.
3. **Fallback** when a replacement is missing or fails to compile (Keen original).
4. **Overlay directory** searched before `Content/Shaders` — supplied by Anomaly assets and by [Pulsar shader packs](ShaderPacks.md).

Do **not** steal: one exclusive pack that owns the whole frame. Pulsar loads many plugins.

### Other frameworks (what to copy)

| Framework | Strategy | Fits SE? |
|-----------|----------|----------|
| **Iris / OptiFine** | Replace the world renderer; named programs + fallbacks | No for the whole frame. Yes for *Anomaly-owned* extra passes. |
| **ReShade** | Hook Present; never touch game geometry shaders | Fine for FX. Useless for object velocity. |
| **3DMigoto / Special K** | Hash of compiled DXBC → replacement HLSL | Works, but hashes die on every Keen compile. Prefer Keen identity (`path` or `material + pass + flags`). |
| **Engine include path** | Extra include dir + `#define`; shared `.hlsli` grows | **Best default** for piggyback (`SV_Target3` velocity). |
| **Ubershader / permutation** | One template × flags (`CacheGenerator.xml`) | Stay inside Keen’s compiler; do not invent a second one. |

SE is already an ubershader engine. Anomaly **rides that compiler**.

---

## Four layers behind one compile hook

Ship layer 0, then 1. Layers 2–3 are the public shader API; they reuse the same hook.

```
Keen MyShader compile
        │
        ▼
┌───────────────────────────┐
│ 0  Compile intercept      │  include dirs, macros, overlay resolve, cache key
└─────────────┬─────────────┘
              ▼
┌───────────────────────────┐
│ 1  Additive injection     │  shared GBuffer stages, extra MRT, history SRV
└─────────────┬─────────────┘
              ▼
┌───────────────────────────┐
│ 2  Named replacement      │  overlay file for a stage / (material, pass)
└─────────────┬─────────────┘
              ▼
┌───────────────────────────┐
│ 3  Owned passes + buffers │  camera MV, later extras; IVelocityBuffer registry
└───────────────────────────┘
```

### 0 — Compile intercept (must exist)

Hook the single compile entry in `VRage.Render11` (include root / permutation). Every path goes through it:

- Extra include directories (Anomaly `Shaders` asset, then registered [shader packs](ShaderPacks.md)).
- Extra defines (`ANOMALY_VELOCITY`, later plugin-requested flags — [Extensibility.md](Extensibility.md) slice N).
- **Overlay resolve**: if a pack registered `Geometry/Passes/GBuffer/PixelStage.hlsli`, compile that instead of Keen’s file.
- Cache identity must include overlay + define set + pack fingerprints, or Keen will serve stale DXBC.

After assets and pack overlays are active, Anomaly requests one frame-boundary
refresh through both `MyShaders.Recompile()` and `MyMaterialShaders.Recompile()`.
Keen has two resident geometry-shader owners: Stage 2 bundles hold shader IDs
updated by the former, while old-pipeline material bundles own native shader
objects rebuilt by the latter. Shaders created before Pulsar initialized Anomaly
are therefore rebuilt through the same compile intercept. This neither clears
Keen's source-keyed cache nor introduces a second compiler or renderer.

This is Iris’s “patch at load,” keyed by **path + permutation**, not DXBC hash.

### 1 — Injection (default; velocity lives here)

Do not expose “replace Standard pixel” for velocity. Inject only:

- `Geometry/Passes/VertexStage.hlsli` and `PixelStage.hlsli` as thin
  dispatchers: GBuffer resolves through Anomaly; every other pass resolves to Keen.
- `Geometry/Passes/GBuffer/VertexStage.hlsli`
- `Geometry/Passes/GBuffer/PixelStage.hlsli`
- `GBuffer/GBufferWrite.hlsli`

Wrapped in `#ifdef ANOMALY_VELOCITY` so **Depth** permutations never grow a fourth target.

Iris analog: injecting uniforms into every `gbuffers_*` program, not shipping a unique file per block type.

### 2 — Wholesale replace (opt-in, narrow)

For a plugin that truly wants a different Standard GBuffer PS:

- Register by **Keen identity**: relative path, or `(material, pass, flag mask)`.
- Anomaly substitutes at compile. On failure, log and fall back to Keen.
- **One owner per key.** Two plugins claiming `Materials/Standard/Pixel.hlsl` is an error with names in the log, not last-writer-wins silence.
- Prefer replacing **shared stages** (`GBufferWrite.hlsli`) over per-material files.

Iris analog: “this pack provides `gbuffers_terrain.fsh`,” scoped to Keen’s permutation compiler instead of a full renderer swap.

Defer a second plugin’s wholesale Standard/Pixel fork until someone needs it; the overlay table is the same registry Anomaly’s GBuffer inject uses as fallback when no pack claims that path.

### 3 — Owned programs + published buffers

Fullscreen camera MV, owned linear depth / Hi-Z / history color, debug vis: **Anomaly shaders**, not Keen overlays. Settings **Debug buffer** blits a catalog texture onto the backbuffer after `DrawGameScene` (`CatalogDebug.hlsl`) at `ViewportResolution` so it covers DLSS/DRS output. Velocity mode binds resolved GBuffer depth so sky (complementary 0) is dark grey instead of rest-gray or saturated camera MVs. `GBufferVelocityRaw` bypasses the camera-fill composite; the developer Velocity probe can distinguish the frame clear, force a known value through the active velocity VS/GBuffer PS/Target3 path, clear the final target once on Keen's immediate context after all deferred geometry command lists execute, or visualize previous-world lookup coverage. `MrtWrite` clears to -X, selects +X in the VS and again at the final pixel output through PS b7, repeats the native MRT/constant-buffer binds, and substitutes replace blending for the velocity and audit outputs while preserving Keen's Target0–2 blend behavior; pink/cyan/gray therefore distinguish a successful Target3 write, no geometry write, and an explicit zero. `VelocityPipelineAudit` binds a reserved RGBA16F `SV_Target7` only during the audit and displays six full-scene panels from that same Keen GBuffer pixel invocation: selected Target3 checkpoint, pixel-executed flag (green=true, dark red=false), PS b7 enable, untouched VS→PS velocity, a PS-b7 marker written through known-good GBuffer0, and raw GBuffer0. Pack attachments consequently use `SV_Target4–6`. Velocity constants use an Anomaly-owned ring per render context, with one dynamic-map per entry per frame; this isolates b6 bytes from Keen's size-keyed object-CB cache and deferred discard aliasing. The final pixel probe uses a dedicated immutable 16-byte b7 payload (`float4(value.xy, enabled, 0)`), independent from the 224-byte VS b6 layout and without a probe-specific shader permutation or cache. Status reflects both layouts and disassembles resident DXBC, reporting executable cb6/cb7 reads and Target0/3/7 writes separately from reflection signatures. While `MrtWrite` is active, the developer-only `Native draw state` line queries every intercepted draw's native VS, PS, b6, b7, Target3, and (during audit) Target7 bindings and reports match/null/mismatch counts. It also reads the actual native blend object and reports independent blending plus Target3/Target7 write-mask coverage, releasing all queried interfaces immediately. Every probe is selected at runtime and never rebuilds shaders. Anomaly performs the one required startup refresh by queuing Keen's native `ReloadEffects` render message; Keen recompiles both shader owners before scene drawing and marks renderables dirty. Status reports that lifecycle as `resident=queued on Keen render thread`, `running`, or `done#N`, plus the measured GBuffer PS compile count as `overlay-GBuffer-PS=N` and scheduler-end execution as `pass-end-clear=1`. Stage 2 packs t15 on the CPU during prepare, then records its GPU upload on the Stage 2 GBuffer deferred context immediately before draws; parallel prepare workers never map Keen's global immediate context. Consumers bind `IVelocityBuffer` or `BufferCatalog.Active(name)` by well-known type name ([ClientPlugin/Velocity/README.md](../ClientPlugin/Velocity/README.md), [ClientPlugin/Buffers/README.md](../ClientPlugin/Buffers/README.md)). Other plugins should rarely compile Keen permutations; they should consume textures Anomaly already bound.

Pack fullscreen effects ship `Fullscreen/<Slot>/*.hlsl`. Anomaly compiles and draws them (`FullscreenPassRegistry`). Packs do not call `Draw` or create RTs. C# `OwnedPassRegistry.Register` stays the escape hatch. `OwnedPassPhase.BeforeFullscreen` runs **before** data-driven programs; AfterFullscreen (default) runs after. `FullscreenPassRegistry.SetEnabled(id, bool)` is the pack checkbox (independent of two-Replace fail-closed; a live Replace is exclusive that frame without clearing Isolated siblings). `SetScale(id, scale)` / json `passes[].scale` sizes IsolatedAdd / IsolatedMix / IsolatedSub / PublishOnly / DirectAdd / Chain isolated RTs to 1, 0.5, or 0.25 of the slot scene size (Replace stays dest-sized). Sample full-res GBuffer and depth by UV (`AnomalyScenePixel`). AfterLighting IsolatedSub of tiled lights uses `AnomalyLightingUv(SV_Position)` like Keen `LightPoint.hlsl`. BRDF reconstruct is `AnomalyLightingViewPos`; contact reconstruct is `AnomalyLightingViewPosUnjittered` and samples depth with `AnomalyViewToDepthUv`. Contact dither is unscaled `AnomalyIgnWorld` (unjittered view). IsolatedSub is photometric `sum L_i*(1-vis_i)` of the brightest N; march may continue past `light.range`, energy does not. Do not scale contact step length with `AnomalySafetyScale`. Convert a full-res pixel radius to UV with `AnomalySceneUvOffset` (not `AnomalyInvPassSize`). AfterFullscreen C# that draws a scaled RT must call `FullscreenPassRegistry.DrawFullscreen(rc, width, height)` — Keen `DrawFullscreenQuad()` with no viewport calls `SetScreenViewport()` and stores only the top-left of UV 0–1. HDR slots (AfterLighting through BeforeTonemap) skip LCD / TargetView / TargetCamera so packs do not need their own view guard. `TryGetProgramStatus(id, out string)` is the pack Status bind; compile failures stay on Anomaly Status as `id!compile`. Keen `Math.hlsli` already defines `rand(float2)` — do not redeclare it after `#include <AnomalyFullscreen.hlsli>`.

| Compose | Who | Dest |
|---------|-----|------|
| `IsolatedAdd` (default) | Many, additive | Scratch then `src + dest` into `LBuffer` (HDR slots) or the AfterTonemap result |
| `IsolatedMix` | Many, over | Scratch then `src + dest * (1 - src.a)`. `src.rgb` must already be **LBuffer energy**. Light with `AnomalySunColor * AnomalySunDiffuse`. In-cloud day fill is `AnomalyVolumeAmbient()`. Planet-night is `AnomalyVolumeNight(albedo, sunVis)` (AJ × 0.05, Keen night ambient). Sun vis is `AnomalySunTransmittance` (monotonic squared limb `sqrt(2h/r)`; OD only after geo is ~1). Dest darken from a volume is `AnomalyVolumeSunShadow` (catalog `volumeSunShadow`). High RGB + low alpha is fireflies. 0–1 albedo with high alpha replaces HDR sky. `passes[].scale` is pixel cost, not lighting. |
| `IsolatedSub` | Many, occlude | Scratch then blend `dest.rgb * (1 - saturate(src.rgb))` onto dest (same dest RTV as Replace). Pack writes a **0–1 dest fraction** (`AnomalyIsolatedSub` / `AnomalyIsolatedSubEnergy`). IsolatedSub blits dest for t0 when dest aliases LBuffer. Umbra uses `temporal` InColor+Reactive |
| `Chain` | Many, ordered | Each samples the previous isolated; last copies to dest |
| `PublishOnly` | Producer | Scratch only; catalog `pass.<id>` / `fullscreenIsolated` |
| `Replace` | One owner | Fail closed if two claim the slot; other compose on that slot is disabled |
| `DirectAdd` | Opt-in | Isolated then additive merge (same bus) |

Fixed bus: t0 scene, t1 `linearDepth`, t2 `velocity`, t3 `reactiveMask`. HDR slots bind Keen GBuffer at t4–t6, tiled point lights at t10/t11 (`AnomalyPointLights` / `AnomalyTileIndices`), and Keen `FrameConstants` at b0 (`AnomalyFullscreen.hlsli` includes `Frame.hlsli` on HDR slots so `frame_` is declared). AfterTonemap / AfterUpscale bind catalog `avgLuminance` / `bloom` / `dirt` at t4–t6 (captured on `MyToneMapping.Run` even when SE-DLSS skips Keen SDR). Pack extras: `FullscreenPassRegistry.RequestSrv(id, catalog, slot)` at t7–t9 (json `passes[].binds`). HLSL defaults to `Texture2D AnomalyPackSrv0–2`; `#define ANOMALY_PACK_SRVn_TYPE Texture3D` before `#include <AnomalyFullscreen.hlsli>` to bind a volume. Catalog `litMips` is generated at AfterLighting when a live program bound that name, `RequestLitMips` was called, or Debug view is LitMips. `RequestOccupancy` / `RequestPointShadows(maxLights, faceResolution)` fill `occupancy` and `pointShadowAtlas` after BeforeFullscreen (default cube cap 4 / face 128, max 64 / 256; mesh map always, AABB when `RequestWorldBoxes`). `historyDepth` is the unread linear ping-pong. b6 extras (pass size + `AnomalySceneSize` + `AnomalySunColor` / `AnomalySkyAmbient` / `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil`, 320 B), b7 uniforms (`SetUniforms`, 64 floats, `AnomalyPassUniform0–15`; 0–7 unchanged). `#include <AnomalyFullscreen.hlsli>`. AfterUpscale t0 is catalog `upscaledColor` when the unique consumer called `NotifyUpscaleComplete(rc, color)`. Replace draws into that dest when it is a different resource from t0 (optional `__compute_shader`); when dest aliases t0 (DLSS in-place) Anomaly grades into UAV scratch then copies so `__result` stays graded. Display-without-upscale grades `LBuffer` at `ResolutionI`. Isolated still publishes. Without a dest, t0 falls back to `LBuffer` (wrong pixels at output res — display tenants must check `HasUpscaledColor`).

Iris analog: `composite` / `final` — extra passes the framework owns.

---

## Public stage names

Do **not** generate 215 replace slots. Public surface is **semantic**:

| Stage name | Maps to Keen |
|------------|----------------|
| `GBuffer` | Shared GBuffer pass + `GBufferWrite` / `GBuffer.hlsli` (not a Materials fork) |
| `Depth` | Depth pass — **no extra MRT**; replacements must stay 0-target |
| `Forward` | Probe / far forward |
| `Highlight` | Selection outline |
| `Transparent` | OIT pass + resolve |
| `TransparentForDecals` | Glass receiving decals |
| `Lighting.Dir` / `Lighting.Point` / `Lighting.Spot` | Deferred lighting programs |
| `Post.Tonemap` / `Post.HBAO` / `Post.SSAO` / `Post.Bloom` / `Post.FXAA` / `Post.EyeAdaptation` / `Post.Luminance` / `Post.ChromaticAberration` | Named post files |
| `Anomaly.CameraVelocity` | Owned fullscreen (`CameraVelocity.hlsl`, `Fullscreen.hlsl`) |
| `Anomaly.LinearDepth` | Owned linear depth + Hi-Z (`LinearDepth.hlsl`, `HiZDownsample.hlsl`) |
| `Anomaly.HistoryColor` | Owned previous-frame HDR copy (`HistoryCopy.hlsl`) |
| `Anomaly.LitMips` | Owned this-frame HDR mip chain (`HistoryCopy.hlsl` + `GenerateMips`) |
| `Shadows` | CSM screen mask (`Shadows/Shadows.hlsl`, `Csm.hlsli`) |
| `Atmosphere` | Bruneton aerial perspective (`Transparent/Atmosphere/*`) |
| `Decals` | Deferred decal volumes (`Decals/Decals.hlsl`) |
| `GPUParticles` | GPU particle raster (`Transparent/GPUParticles/*`) |
| `EnvProbe` | Probe blend / prefilter (`EnvProbe/*`) |
| `Foliage` | Grass/rock cards (`Foliage/*`) |

Escape hatch: overlay by Keen-relative path for a one-off file (`Overlay/Geometry/Materials/Standard/Pixel.hlsl`).

Pack layout: `Overlay/<Stage>/<file>` (unique basename or suffix). Implemented in `ClientPlugin.Shaders.ShaderStages`.

Fallbacks: replacement missing or compile `#error` → Keen original. A Depth replacement that adds `SV_Target3` must fail compile, not ship.

---

## Composition (multi-plugin)

Iris packs are **exclusive**: one pack at a time. Pulsar loads **many plugins**. If SE-DLSS, a TAA plugin, and a “replace Standard” pack all patch `PixelStage.hlsli`, that is a merge conflict.

Rules:

1. **Anomaly owns extra GBuffer attachments.** Plugins request a slot (“RG16F velocity”) rather than splicing `SV_Target3` themselves.
2. **Defines are merged by Anomaly**, not by each Harmony patch on `MyShader`.
3. **Replace is exclusive per key**; inject is additive behind Anomaly-owned includes (`Anomaly/Extras/GBuffer.hlsli`, aliased as `Anomaly/GBufferExtras.hlsli`).
4. **Consumers do not Harmony-patch `DrawGameScene`, `MyTransparentRendering.Render`, `MyAtmosphereRenderer`, `MyToneMapping.Run`, or instance updates.** They bind registry textures or register an [owned-pass](#owned-pass-scheduler) draw. Anomaly owns those Harmony prefixes and the unbind.
5. **`ClearState` / DRS / device reset** stay Anomaly’s problem. Replacements must not leak RT/SRV ([Rich HUD](https://github.com/ZachHembree/RichHudFramework.Client)).
6. **Anomaly-owned GBuffer write stages** (`Geometry/Passes/GBuffer/*Stage.hlsli`, `GBuffer/GBufferWrite.hlsli`) stay Anomaly’s unless a pack sets `exclusive: ["GBuffer"]`. **Read wraps** (`GBuffer/GBuffer.hlsli`, `Surface.hlsli`) need `exclusive: ["GBuffer"]` or `["Lighting"]`. **`Lighting/Light.hlsli`** needs `exclusive: ["Lighting"]`. **`Transparent/Atmosphere/AtmosphereCommon.hlsli`** needs `exclusive: ["Atmosphere"]`.
7. **Compile failure rolls back that pack** (sentinel per live named stage after apply; in-game overlay errors log `pack=<id>` and disable the owner).
8. **Inject/overlay of Atmosphere does not fix DLSS.** Animated emission after `MyRenderScheduler.Done` is invisible to the frozen velocity buffer unless the pass sets `ContributeVelocity` / `Reactive`. The unique upscaler owns Halton jitter. When an AfterUpscale `Display` tenant is registered, that upscaler should evaluate **pre-tonemap HDR** and publish the dest; otherwise it may still evaluate LDR after Keen tonemap.
9. **Geometry GBuffer reserves VS b6.** Developer `MrtWrite` additionally reserves PS b7 for a dedicated 16-byte final-output sentinel during the geometry pass. Fullscreen b7 remains the unrelated `SetUniforms` bus in fullscreen-pass scope.
10. **Pack workarounds the next shader will also need belong on Anomaly.** File Slice AI / [wiki/Framework-gaps.md](../wiki/Framework-gaps.md) in the same turn. Append extras CB fields, compose modes, or `AnomalyFullscreen.hlsli` helpers. Do not Harmony-patch Keen pass methods for lighting, IsolatedMix energy, or march LOD. `IsolatedMix` `src.rgb` is `LBuffer` energy, not 0–1 albedo.

[SmoothFrames](https://github.com/WhiteFang34/SmoothFrames) also patches the render thread. Do not assume exclusive ownership of `DrawGameScene`.

---

## Frame graph (what actually runs)

Order is Keen’s, not a pack’s. Velocity and derived depth extras freeze at scheduler Done — **before** atmosphere, clouds, OIT, and billboards.

| Moment | Who | What is live |
|--------|-----|----------------|
| GBuffer (+ velocity MRT) | Keen + Anomaly inject | Object MVs on geometry pixels |
| Lighting | Keen + Lighting wrap / extras | Catalog velocity at **t5**, extras CB b6 |
| `MyRenderScheduler.Done` | Anomaly owned | Camera fill + composite → publish `velocity`; `linearDepth` / `hiZ` **frozen** |
| AfterLighting | `OwnedPassRegistry` + `FullscreenPassRegistry` | Prefix `MyTransparentRendering.Render` on Keen’s transparent deferred worker (`DoWork` / `AcquireRC`). HDR `LBuffer`. Atmosphere not yet. BeforeFullscreen C#, then `litMips` if wanted, then data-driven `Fullscreen/`, then AfterFullscreen C#. HDR slots skip LCD / TargetView / TargetCamera. Use `ctx.Rc`. Anomaly redirects `MyRender11.RC` / `Device.ImmediateContext` to that `rc` during the callback (logs once). |
| Atmosphere | Keen + Atmosphere wrap | `DensityLut` at **t5** (do not steal). Anomaly velocity at **t6**, extras from t7, extras CB b6. Per-planet clouds inside `RenderGBuffer`. |
| AfterAtmosphere | `OwnedPassRegistry` | Postfix `RenderGBuffer` **after unbind**. Records on Keen’s transparent deferred worker (`AcquireRC("MyTransparentRendering")`). Use `ctx.Rc`. Same immediate-context redirect as AfterLighting. Aurora-class draws set their own t20–t25. |
| Clouds / OIT / additive-top | Keen | Transparent emission. **No new MVs** unless a pass contributed. |
| AfterTransparent | `OwnedPassRegistry` | Postfix `Transparent.Render`. |
| BeforeTonemap | `OwnedPassRegistry` | Prefix `MyToneMapping.Run` (Priority.Last). HDR, internal res. |
| Tonemap | Keen (or skipped) | HDR → LDR at internal / DRS size — **skip** when `HasDisplayTenant && !HasUpscaleConsumer`. Unique upscalers may skip Keen themselves after `NotifyUpscaleComplete`. Every skip still needs an `IBorrowedCustomTexture` dest so `DrawGameScene` can highlight / FXAA / copy (`borrowedCustomTexture.Linear` / `.SRgb`). Anomaly’s `Run` postfix adopts the notified dest when the skipper omitted `__result`; otherwise it borrows the Display wrap. Keen’s dest is 8-bit UNORM; Anomaly wraps `R16G16B16A16_Float` when the dest cannot store scRGB above 1 (same wrap on FXAA / CA borrows). |
| AfterTonemap | `OwnedPassRegistry` | Postfix `Run` (Priority.First) — **before** upscale evaluate. Internal LDR if Keen ran. |
| Upscale evaluate | Unique consumer (`ClaimUpscale`) | `hdrColor` / `LBuffer` when a `Display` tenant exists; else LDR after Keen. Jitter owner. |
| AfterUpscale | `NotifyUpscaleComplete(rc, color)` | Output res after an upscaler. Catalog `upscaledColor` + fullscreen t0 + `ctx.SceneColor`. Display-without-upscale grades `LBuffer` into the borrowed dest at `ResolutionI` before `DrawGameScene` continues. Fallback at `DrawGameScene` postfix (native res, no dest) only if nobody notified. |
| History + debug | Anomaly | `historyColor` copy and catalog debug at `DrawGameScene` postfix (debug is Priority.Last, `ViewportResolution` on the backbuffer). |

Jitter owner is **SE-DLSS** (`Projection.M31` / `M32`). Anomaly reads it into `FrameTemporal` and republishes an **unjittered** view-projection on the extras CB. Packs must not patch the projection.

Transparent / aurora emission is **color-in, motion-out** unless the pass sets `Reactive` and/or `ContributeVelocity`. IsolatedAdd (and IsolatedMix / DirectAdd / PublishOnly) with `ContributeVelocity` reconstructs camera MVs from isolated.a (view-space hit distance in meters) and composites them over catalog `velocity`. `Reactive` stamps dilated isolated luma into `reactiveMask` automatically (history reject — flickers under spectator translation). Overlaying Atmosphere HLSL alone cannot invent motion vectors for DLSS.

---

## Owned-pass scheduler

Well-known types: `ClientPlugin.Shaders.OwnedPassRegistry` and `ClientPlugin.Shaders.FullscreenPassRegistry`. Reflection-friendly `Register(id, slot, priority, temporalPolicy, draw)` and `Register(..., phase)` (`OwnedPassPhase`: BeforeFullscreen = 0, AfterFullscreen = 1). `draw` receives `OwnedPassContext` boxed as `object`. Prefer `Fullscreen/<Slot>/*.hlsl` + `passes[]` so the pack does not own a draw. Anomaly owns the Harmony and `Draw(3)`; packs do not. Order at each slot: BeforeFullscreen C# → `litMips` (HDR, if wanted) → data-driven Fullscreen → AfterFullscreen C#. `SetEnabled(id, bool)` skips the fullscreen draw without unregistering. `TryGetProgramStatus(id, out string)` reports live / pack-off / compile-failed. HDR slots skip LCD / TargetView / TargetCamera.

| Slot | Hook | Typical use |
|------|------|-------------|
| `AfterLighting` | Prefix `Transparent.Render` | HDR after lights, before atmosphere |
| `AfterAtmosphere` | Postfix `Atmosphere.RenderGBuffer` | Additive curtains / aerial extras. Same deferred `rc` as `Transparent.Render`. |
| `AfterTransparent` | Postfix `Transparent.Render` | After OIT + top billboards |
| `BeforeTonemap` | Prefix `ToneMapping.Run` (Last) | HDR grade |
| `AfterTonemap` | Postfix `Run` (First) | Internal LDR, before upscale evaluate |
| `AfterUpscale` | `NotifyUpscaleComplete(rc, color)`, display-without-upscale dest, or DrawGameScene fallback | Output-res display / composite. Read `upscaledColor` when notified; native `LBuffer` when grading into the dest `DrawGameScene` copies. |

`TemporalPolicy` flags (OR together): `InColor`, `ContributeVelocity`, `Reactive`, `Display`.

- **InColor** — writes `LBuffer` (HDR) or LDR after tonemap. Temporal consumers see the color.
- **ContributeVelocity** — IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly reconstruct camera MVs from isolated.a (view-space hit distance, meters; 0 = no overlay) and republish `velocity` so SE-DLSS sees curtain-depth parallax instead of far-plane sky fill. C# owned passes may still call `OwnedPassContext.ContributeVelocity(overlaySrv, maskSrv)` (mask &gt; 0.5).
- **Reactive** — IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly stamp dilated isolated luma into catalog `reactiveMask` (R8, cleared to 0 at first use each frame **on the slot’s `rc`**). AfterLighting / AfterAtmosphere / AfterTransparent record on a deferred worker; Anomaly clears/stamps on that `rc` and redirects `MyRender11.RC` during the callback. C# owned passes write the RTV via `ctx.Rc`. High = do not trust history. SE-DLSS must bind this itself; Anomaly only publishes it. Replace compose does not auto-stamp (would mark the whole dest).
- **Display** — AfterUpscale display-referred grade (BT.2390 / scRGB). `HasDisplayTenant` is the signal for the unique upscaler to evaluate HDR and skip Keen SDR. The pass must sample `ctx.SceneColor` / `upscaledColor`.

The unique upscaler calls `ClaimUpscale(id)` at init and `NotifyUpscaleComplete(rc, color)` after evaluate. A second claimer fails closed. Two notifiers would fight (`upscaleNotified` is once per frame). Display without an upscaler skips Keen `Run` but returns a dest `DrawGameScene` can copy (Keen’s borrow when already fp16; otherwise an Anomaly fp16 UAV wrapper), grades `LBuffer` into it, and marks notified. Display **with** an upscaler: if that plugin skips `Run` (Harmony prefix `false`) and does not set `__result`, Anomaly’s postfix adopts the notified dest so `DrawGameScene` does not NRE. If nobody notifies, Anomaly runs AfterUpscale once at `DrawGameScene` postfix with `LBuffer`. Anomaly does not present.

`FrameTemporal` (well-known): `JitterX` / `JitterY`, `UnjitteredViewProj`, `PrevViewProj`, `CameraToWorld`, `ProjScale`, `SafetyScale`, `SunColor` / `SunToward` / `SunDiffuse` / `SkyLuma` / `SkyAmbient` / `PlanetAirTop` / `VisualAtmoCeil`, `InvalidateHistory()`. Same extras CB fields for lighting, atmosphere, and post (`AnomalyLightingJitter`, `AnomalyUnjitteredViewProj`, `AnomalyPrevViewProj`, `AnomalyLightingFrameIndex`, `AnomalyCameraToWorld`, `AnomalyProjScale`, `AnomalySafetyScale`, `AnomalySunColor`, `AnomalySunDiffuse`, `AnomalySunToward`, `AnomalySkyLuma`, `AnomalySkyAmbient`, `AnomalyPlanetAirTop`, `AnomalyVisualAtmoCeil`). Append-only; extras are **320 B**. `PlanetAtmosphere` (well-known): game-thread nearest `HasAtmosphere` / CloudLayers planet; `TryGetRadii(worldCenter, matchMeters, out airTop, out visualCeil)`. Radii are meters from the planet center. Fail closed to 0. HLSL `AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade` (missing extras leave pack uniforms). `LocalCharacter` (well-known): game-thread local player actor id for mesh maps; 0 otherwise. `RequestPointShadows` stamps that mesh into `pointShadowAtlas`. `AnomalySafetyScale` is camera translation / look (1 = calm, 0 = cut). Drops match this frame; recovery is damped (`1/16` per frame) so march step counts do not chatter. March packs call `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)` — uniform camera-to-volume × `AnomalySafetyScale`. Do not globally floor SafetyScale (that re-opens a slam TDR). Keep a few steps when far so the volume does not vanish; IsolatedAdd `ContributeVelocity` (packed hit `t`) locks the rest. Per-ray hit distance is the wrong cheapening metric (grazing warps keep the compile-time max). Planet-disk sun occlusion is `AnomalySunVisibility(posCamRel, occluderCenterCamRel, occluderRadius, lightWrapMeters)` — wrap is atmosphere thickness, **capped at 12% of occluder radius**; 3-arg uses that 12% directly. Keen CSM is camera-local and does not cover a planet from orbit. IsolatedMix night fill is `AnomalyVolumeAmbient()` (`AnomalySkyAmbient` = `SunColor * 0.028`; never hdr-lift `AnomalySkyLuma`; never scale by `AmbientForwardPass`). Load-time lint warns on `Fullscreen/` `while` or a `for` without an integer literal / `#define` cap — compile still proceeds.

Crash breadcrumbs: well-known `ClientPlugin.ShaderFramework.RenderTrace`. Packs may `Begin`/`End` interned names (no per-frame alloc) and `Dump`/`DumpIfLost` on fail. Anomaly records owned slots and fullscreen program ids in a 64-entry ring with **no disk I/O on the success path**. The first lost-device / `DrawGameScene` / `Present` unwind freezes the ring and writes one `Anomaly RenderTrace dump at …` line to `SpaceEngineers.log` (plus `Anomaly.debug.log` in DEBUG). A trailing `name>` without `name<` is the in-flight CPU submit, not proof of the hung shader — GPU work is async. `try/catch` around `Draw(3)` that already returned cannot name a later TDR.

Injection + exclusive replace can coexist: velocity inject still applies to a replaced Standard pixel **only if** that pixel still includes `Passes/PixelStage.hlsli`. A full-file replace that omits the include opts out of extras — document that.

---

## What this repo implements first

Keep the [PLAN.md](PLAN.md) cut:

1. Hook compile (include dir + `ANOMALY_VELOCITY` + Depth still compiles). That *is* the framework.
2. Ship velocity as **injection**, not as a Standard/Pixel fork.
3. Public shader API shape: Pulsar named assets + pack `Register` + named-stage replace table + buffer registry. Packs are Pulsar plugins that depend on Anomaly; see [ShaderPacks.md](ShaderPacks.md). Overlay replace is live (fail closed on conflict). A sentinel compile per live named stage rolls back a pack that breaks that stage.

After that cut: generalize the same hook for more tenants — stage-scoped inject, pack defines, attachment slots, lighting/atmosphere wraps, bind registry, owned-pass scheduler, buffer catalog, data-driven `Fullscreen/` programs. Order: [Extensibility.md](Extensibility.md).

Iris’s lesson is semantic stages + compile-time rewrite + fallback, sitting on a renderer the framework controls. Anomaly’s rewrite sits on **Keen’s** renderer, because that renderer is the thing we cannot afford to clone.

## Terminal config (Rich HUD)

Well-known type: `ClientPlugin.RichHud.TerminalConfigRegistry`. When [Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081) is in the world, Anomaly mounts Pulsar MyGui options under **Anomaly Shaders → Anomaly** (`Settings` and `Velocity Debug`). Packs and consumers call `RequestPage(title)` from `LoadAssets` or `Init` and add a sibling page on that root. Empty and reserved titles (`Anomaly`, `Settings`, `Velocity Debug`) fail closed. Corner status uses `ClientPlugin.RichHud.HudOverlayRegistry.Register(id, get)` — the getter returns a cached string on the HUD draw thread and must not write a `.cfg`. Master is optional; MyGui stays the fallback. Packs must not vendor a second client or list Master as a Pulsar `DependencyId`. Slider setters must not serialize or write a `.cfg` on the HUD or Update thread — cache one `XmlSerializer`, omit `CustomValueGetter` on every host control (dropdown getters cannot be Master’s `ListBoxEntry`), and let `FlushPending` write on a worker. Dropdown setters pull sibling controls from their getters once (`Refresh()` does the same after a button writes several fields). See [TerminalConfig.md](TerminalConfig.md), [ShaderPacks.md](ShaderPacks.md), and the wiki [Terminal config](../wiki/Terminal-config.md).


### Point-shadow atlas sampling

Include `AnomalyPointShadows.hlsli` and use
`AnomalyPointShadowVisibility(atlas, row, directionWorld, distanceMetres, biasMetres)`.
Faces are +X, -X, +Y, -Y, +Z, -Z. The producer uses view-space face directions;
its raster UV includes the D3D Y inversion. The helper performs four weighted
comparisons without reading another face, light row or the header. It is not a
physical penumbra filter. `BufferCatalog.PointShadowStatus` exposes caster/draw
diagnostics. One complete opaque character LOD is submitted independently of
first-person visibility. Detailed world-mesh casters remain future work.

The legacy name `AnomalyLightingViewPosUnjittered` does not mean stripping jitter
from the raster inverse: it now reconstructs using the live projection, then
`AnomalyViewToDepthUv` returns the original depth coordinate.


### Contact traversal and atlas quality revision (2026-09-18)

`AnomalyContactShadows.hlsli` exports `AnomalyContactVisibility(depth, receiverView,
geometricNormalView, towardLightView, rayLengthMetres, depthThicknessMetres,
pixelBudget)`. Include AnomalyFullscreen first. It is for current-frame camera
depth only: it uses that frame's projection/reconstruction. Projected pixel
intervals have perspective-correct depth bounds; exhaustion truncates/fades the
ray instead of widening the stride. Receiver tangent-plane rejection and a small
bias replace camera-depth separation gates. Maximum budget is 192 intervals.
History depth is not supported by this helper; moving previous poses must not
be treated as independent casters. Screen-space visibility is incomplete.

PointShadowPass supports face sizes through 1024. Selection is constrained to
D3D11 dimensions and roughly 96 MiB (RGBA32F plus header): 16 lights at 256,
4 at 512, 1 at 1024. Requested cap and effective selection can differ. Detailed
world-mesh casters, stable light IDs and measured GPU update budgets remain open.
ScreenSpaceShadows is the first tenant. Its atlas visibility no longer depends on
the contact top-N budget. WARP tests cover the shared sampler and contact helper;
Light Test visual/performance acceptance remains pending.


### Unified character shadow and light stability (2026-09-18)

This supersedes the first-person-only restriction above: the same skinned atlas
caster runs in both perspectives. Screen-space contact is not a complete
third-person character-shadow substitute.

Atlas header texels 0..63 retain (view-space light position, range). Texel 64
now contains (camera-relative world caster center, valid=1); texel 65 contains
world-axis half extents. Metadata is zero unless mesh draws completed, and is
written after capture. AnomalyContactVisibilityExcludingBounds accepts these
bounds plus the current lighting frame view-to-world rotation. Only a matching
atlas light in Combined mode excludes this caster from depth contact; missing
atlas and ContactOnly retain the original depth path. This is a bounded spatial
approximation: nearby geometry inside the character bounds can also be excluded;
a per-pixel caster identity mask remains the precise long-term solution.

Atlas selection uses the render character center, in-range preference, stable
actor-ID ties and incumbent retention (0.81 squared-distance factor). Status
includes selected actor IDs. Sampling uses the live lighting inverse-view matrix.
GPU tests cover rotated exclusion and preservation of blockers outside bounds.
Live flicker acceptance is still pending; these tests do not reproduce frame
generation, temporal reconstruction or dynamic light culling.


### HDR stage ordering correction (2026-09-18)

The old AfterLighting prefix on MyTransparentRendering.Render ran during
parallel command-list recording. MyRenderScheduler schedules lighting resolve
and transparency independently; CPU light capture could therefore lag the
shadow pass. Worse, linear depth was produced in Scheduler.Done postfix,
after the HDR shadow consumers had used the previous frame's depth.

AfterLighting now runs in MyGBufferResolver.ConsumeWork postfix on the immediate
render context, after lighting submission. OwnedBuffersPass.Execute produces
current depth at this boundary before pack execution. AfterTransparent runs
in MyTransparentRendering.ConsumeWork postfix, after transparency submission.
The late Scheduler.Done depth invocation was removed; velocity diagnostics and
camera-velocity processing remain there. This changes shared HDR-stage timing
for all Anomaly packs and requires live integration validation.

Player contact policy: the mesh atlas serves both perspectives. Header texel
64.w identifies valid player bounds independent of atlas light allocation;
65.w separately indicates successful mesh capture. Contact excludes player
bounds even for lights without an atlas row and in ContactOnly diagnostic mode
(scene-only contact). This prevents partial player silhouettes returning when
coverage changes. It remains a bounding-volume approximation, not a precise
per-pixel actor mask.

TestShadowFrameOrder.ps1 checks the hook topology against the local decompiled
engine schedule and rejects a return to late depth production. GPU contact and
atlas tests remain green. These checks do not establish visual acceptance.


### Player contact mask (2026-09-18)

Combined now excludes the player using a rendered per-pixel depth mask instead
of atlas-header bounds. PointShadowPass renders the same skinned character into
reserved catalog `playerDepth`, an R32_FLOAT texture at render resolution, using
the current camera projection. Values are Euclidean camera distance; clear is
100000. It costs one extra character render and 4 bytes per render pixel.

AnomalyContactVisibilityExcludingPlayer compares scene-point distance with this
mask at each sampled pixel (3 cm minimum tolerance, scaled by depth). It skips
matching player samples while preserving foreground walls. This is raster-depth
matching, not an engine object-ID buffer; discrepancies at silhouettes remain a
live validation concern. Header bounds remain available but no longer drive the
Shadows pack's exclusion. Pack t7 binds playerDepth instead of unused historyDepth.

PlayerMask debug view uses the same predicate: red means excluded player, dark
green means scene contact remains eligible. PointShadowStatus includes mask draw
count. Combined is atlas player plus scene contact; ContactOnly is scene contact
without player; AtlasOnly remains atlas. Synthetic WARP tests cover player removal
and a foreground wall at the same pixels. In-game acceptance remains pending.


### Camera-distance and cold third-person transform correction (2026-09-18)

Keen MyCullProxy.UpdateWorldMatrix refreshes camera-relative object matrices only
for selected render proxies. The atlas selected a stable highest-detail LOD, so
its cached CommonObjectData.LocalMatrix could be uninitialized on third-person
load or stale after zooming out. This also displaced the player exclusion mask.

PointShadowPass now uploads a private copy of common object constants with the
live actor WorldMatrix minus the current camera in double precision, preserving
Keen's bone-remapping layout. It does not modify the main-view proxy cache. Atlas
LOD remains stable; playerDepth uses CurrentLod to match visible scene geometry.
LOD cross-fade boundaries and unrelated scene-contact distance artifacts remain
live validation concerns. TestCharacterTransform.ps1 compiles the production
transform helper and checks rotated geometry under 0..100 m camera offsets at
million-metre world coordinates.


### Scene-contact camera-distance regression (2026-09-18)

TestContactDistance.ps1 renders a fixed planar blocker/receiver scene on D3D11
WARP while translating the camera. The original shader passed offsets 0/5 m
but weakened at 15 m (visibility 0.412645 instead of <=0.2). Its eight-pixel
end fade covered an increasing fraction of the physical ray with distance.
The fade now uses perspective-correct physical ray fraction (last 10%).

A second, short-range scene still missed hits at a 3 m camera offset after that
fix. Unit-pixel intervals anchored at the ray origin crossed cell boundaries
but sampled just one cell. Traversal now splits at exact X/Y pixel crossings,
so depth overlap is tested within the sampled cell. Subpixel rays receive one
interval rather than being discarded below half a pixel. Budget bounds use a
conservative crossing count; very long projected rays remain budget-limited.

Regression suite now passes 256 receivers at each of ten camera/scale cases:
0/5/15/30/40 m offsets plus a 0.2-scale short-range scene at 0/1/3/6/8 m. It
includes axis-aligned and diagonal rays, lit receivers outside the silhouette,
flat-floor self-hit rejection and finite endpoints. Existing player exclusion
and foreground-wall tests also pass. Off-screen/hidden blockers and unresolved
subpixel geometry remain screen-space limitations. These reproduced failures
are fixed; matching the reported in-game scene still requires visual acceptance.


### Atlas startup diagnostics (2026-09-18)

A third-person startup report showed 14 proxies and 84 atlas draws: mesh
submission was already active. Ultra's RGBA32F atlas budget admits one 1024px
light row out of 46 captured lights despite a requested cap of 10. This does
not establish whether the selected light or mesh raster contents cause the
missing shadow. PointShadowStatus now reports effective/requested capacity and
keeps atlas and playerDepth states separate; the mask can no longer overwrite
the atlas result. Atlas Faces must inspect an allocated row (row 0 in this case).


### Camera-dependent atlas capture (2026-09-18)

Follow-up live evidence rules out a first-person initialization gate: orbiting
the camera in third person changes the raw atlas silhouette violently, while
turning away from the lamp restores its applied shadow. Raw Atlas Faces contains
the character; this is not just a missing draw. Selection remains budget-limited,
but expanding the light budget is not the fix being tested here.

PointShadowPass previously mapped MyCommon.ProjectionConstants for every cube
face and restored the viewer projection. That buffer is also used by Keen's
transparency, occlusion and geometry passes. The atlas/playerDepth renderer now
owns and disposes a separate 64-byte projection CB, binds it at the standard b1
slot, and never maps/restores the engine buffer. Atlas character and optional box
vertices are now relative to each light's world position, subtracted in double
precision; six fixed world-axis cube projections are centered at zero. Thus the
atlas's mesh/projection inputs no longer depend on the viewer. playerDepth keeps
the camera origin and scene projection. Header/sampler coordinates are unchanged.

Validation: both target builds; production transform tests at large world
coordinates and 36 observer positions across all six cube faces; WARP projection,
filter and contact-distance regressions; source integration checks prohibit the
shared projection buffer and enforce the distinct atlas/mask origins. The live
shaking symptom is confirmed; visual acceptance of this correction is pending.
