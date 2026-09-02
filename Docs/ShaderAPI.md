# Anomaly Shader API

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

This is Iris’s “patch at load,” keyed by **path + permutation**, not DXBC hash.

### 1 — Injection (default; velocity lives here)

Do not expose “replace Standard pixel” for velocity. Inject only:

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

Fullscreen camera MV, owned linear depth / Hi-Z / history color, debug vis: **Anomaly shaders**, not Keen overlays. Settings **Debug buffer** blits a catalog texture onto the backbuffer after `DrawGameScene` (`CatalogDebug.hlsl`). Consumers bind `IVelocityBuffer` or `BufferCatalog.Active(name)` by well-known type name ([ClientPlugin/Velocity/README.md](../ClientPlugin/Velocity/README.md), [ClientPlugin/Buffers/README.md](../ClientPlugin/Buffers/README.md)). Other plugins should rarely compile Keen permutations; they should consume textures Anomaly already bound.

Pack fullscreen effects ship `Fullscreen/<Slot>/*.hlsl`. Anomaly compiles and draws them (`FullscreenPassRegistry`). Packs do not call `Draw` or create RTs. C# `OwnedPassRegistry.Register` stays the escape hatch and runs **after** data-driven programs.

| Compose | Who | Dest |
|---------|-----|------|
| `IsolatedAdd` (default) | Many, additive | Scratch then `src + dest` into `LBuffer` (HDR slots) or the AfterTonemap result |
| `IsolatedMix` | Many, over | Scratch then `src + dest * (1 - src.a)` |
| `Chain` | Many, ordered | Each samples the previous isolated; last copies to dest |
| `PublishOnly` | Producer | Scratch only; catalog `pass.<id>` / `fullscreenIsolated` |
| `Replace` | One owner | Fail closed if two claim the slot; other compose on that slot is disabled |
| `DirectAdd` | Opt-in | Isolated then additive merge (same bus) |

Fixed bus: t0 scene, t1 `linearDepth`, t2 `velocity`, t3 `reactiveMask`, b6 extras (same append-only layout as lighting), b7 uniforms (`SetUniforms`, 16 floats). `#include <AnomalyFullscreen.hlsli>`. AfterUpscale has no dest unless a consumer passes one — Isolated still publishes; merge is skipped if dest is null.

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
5. **`ClearState` / DRS / device reset** stay Anomaly’s problem. Replacements must not leak RT/SRV ([Rich HUD](https://github.com/DarkHelmet/RichHudFramework)).
6. **Anomaly-owned GBuffer write stages** (`Geometry/Passes/GBuffer/*Stage.hlsli`, `GBuffer/GBufferWrite.hlsli`) stay Anomaly’s unless a pack sets `exclusive: ["GBuffer"]`. **Read wraps** (`GBuffer/GBuffer.hlsli`, `Surface.hlsli`) need `exclusive: ["GBuffer"]` or `["Lighting"]`. **`Lighting/Light.hlsli`** needs `exclusive: ["Lighting"]`. **`Transparent/Atmosphere/AtmosphereCommon.hlsli`** needs `exclusive: ["Atmosphere"]`.
7. **Compile failure rolls back that pack** (sentinel per live named stage after apply; in-game overlay errors log `pack=<id>` and disable the owner).
8. **Inject/overlay of Atmosphere does not fix DLSS.** Animated emission after `MyRenderScheduler.Done` is invisible to the frozen velocity buffer unless the pass sets `ContributeVelocity` / `Reactive`. SE-DLSS evaluates **LDR after tonemap** and owns Halton jitter.

[SmoothFrames](https://github.com/WhiteFang34/SmoothFrames) also patches the render thread. Do not assume exclusive ownership of `DrawGameScene`.

---

## Frame graph (what actually runs)

Order is Keen’s, not a pack’s. Velocity and derived depth extras freeze at scheduler Done — **before** atmosphere, clouds, OIT, and billboards.

| Moment | Who | What is live |
|--------|-----|----------------|
| GBuffer (+ velocity MRT) | Keen + Anomaly inject | Object MVs on geometry pixels |
| Lighting | Keen + Lighting wrap / extras | Catalog velocity at **t5**, extras CB b6 |
| `MyRenderScheduler.Done` | Anomaly owned | Camera fill + composite → publish `velocity`; `linearDepth` / `hiZ` **frozen** |
| AfterLighting | `OwnedPassRegistry` + `FullscreenPassRegistry` | Prefix `MyTransparentRendering.Render`. HDR `LBuffer`. Atmosphere not yet. Data-driven `Fullscreen/` first, then C# callbacks. |
| Atmosphere | Keen + Atmosphere wrap | `DensityLut` at **t5** (do not steal). Anomaly velocity at **t6**, extras from t7, extras CB b6. Per-planet clouds inside `RenderGBuffer`. |
| AfterAtmosphere | `OwnedPassRegistry` | Postfix `RenderGBuffer` **after unbind**. Aurora-class draws set their own t20–t25. |
| Clouds / OIT / additive-top | Keen | Transparent emission. **No new MVs** unless a pass contributed. |
| AfterTransparent | `OwnedPassRegistry` | Postfix `Transparent.Render`. |
| BeforeTonemap | `OwnedPassRegistry` | Prefix `MyToneMapping.Run` (Priority.Last). HDR, internal res. |
| Tonemap | Keen | HDR → LDR at internal / DRS size |
| AfterTonemap | `OwnedPassRegistry` | Postfix `Run` (Priority.First) — **before** SE-DLSS evaluate. Internal LDR. |
| SE-DLSS evaluate | Consumer | LDR + `VelocityRegistry.Active` (size must match internal DRS). Jitter owner. |
| AfterUpscale | `NotifyUpscaleComplete` | Output res if a consumer notified; else `DrawGameScene` postfix fallback at native res. |
| History + debug | Anomaly | `historyColor` copy and catalog debug at `DrawGameScene` postfix (debug is Priority.Last). |

Jitter owner is **SE-DLSS** (`Projection.M31` / `M32`). Anomaly reads it into `FrameTemporal` and republishes an **unjittered** view-projection on the extras CB. Packs must not patch the projection.

Transparent / aurora emission is **color-in, motion-out** unless the owned pass writes `reactiveMask` and/or `ContributeVelocity`. Overlaying Atmosphere HLSL alone cannot invent motion vectors for DLSS.

---

## Owned-pass scheduler

Well-known types: `ClientPlugin.Shaders.OwnedPassRegistry` and `ClientPlugin.Shaders.FullscreenPassRegistry`. Reflection-friendly `Register(id, slot, priority, temporalPolicy, draw)`. `draw` receives `OwnedPassContext` boxed as `object`. Prefer `Fullscreen/<Slot>/*.hlsl` + `passes[]` so the pack does not own a draw. Anomaly owns the Harmony and `Draw(3)`; packs do not.

| Slot | Hook | Typical use |
|------|------|-------------|
| `AfterLighting` | Prefix `Transparent.Render` | HDR after lights, before atmosphere |
| `AfterAtmosphere` | Postfix `Atmosphere.RenderGBuffer` | Additive curtains / aerial extras |
| `AfterTransparent` | Postfix `Transparent.Render` | After OIT + top billboards |
| `BeforeTonemap` | Prefix `ToneMapping.Run` (Last) | HDR grade |
| `AfterTonemap` | Postfix `Run` (First) | Internal LDR, before upscale evaluate |
| `AfterUpscale` | `NotifyUpscaleComplete` or DrawGameScene fallback | Output-res composite |

`TemporalPolicy` flags (OR together): `InColor`, `ContributeVelocity`, `Reactive`.

- **InColor** — writes `LBuffer` (HDR) or LDR after tonemap. Temporal consumers see the color.
- **ContributeVelocity** — after draw, call `OwnedPassContext.ContributeVelocity(overlaySrv, maskSrv)` to composite extra MVs where mask &gt; 0.5. Republishes `velocity` so SE-DLSS sees them.
- **Reactive** — may write catalog `reactiveMask` (R8, cleared to 0 at first use each frame). High = do not trust history. SE-DLSS must bind this itself; Anomaly only publishes it.

SE-DLSS (or any upscaler) calls `OwnedPassRegistry.NotifyUpscaleComplete()` after evaluate. If nobody notifies, Anomaly runs AfterUpscale once at `DrawGameScene` postfix.

`FrameTemporal` (well-known): `JitterX` / `JitterY`, `UnjitteredViewProj`, `PrevViewProj`, `InvalidateHistory()`. Same extras CB fields for lighting, atmosphere, and post (`AnomalyLightingJitter`, `AnomalyUnjitteredViewProj`, `AnomalyPrevViewProj`, `AnomalyLightingFrameIndex`). Append-only.

Injection + exclusive replace can coexist: velocity inject still applies to a replaced Standard pixel **only if** that pixel still includes `Passes/PixelStage.hlsli`. A full-file replace that omits the include opts out of extras — document that.

---

## What this repo implements first

Keep the [PLAN.md](PLAN.md) cut:

1. Hook compile (include dir + `ANOMALY_VELOCITY` + Depth still compiles). That *is* the framework.
2. Ship velocity as **injection**, not as a Standard/Pixel fork.
3. Public shader API shape: Pulsar named assets + pack `Register` + named-stage replace table + buffer registry. Packs are Pulsar plugins that depend on Anomaly; see [ShaderPacks.md](ShaderPacks.md). Overlay replace is live (fail closed on conflict). A sentinel compile per live named stage rolls back a pack that breaks that stage.

After that cut: generalize the same hook for more tenants — stage-scoped inject, pack defines, attachment slots, lighting/atmosphere wraps, bind registry, owned-pass scheduler, buffer catalog, data-driven `Fullscreen/` programs. Order: [Extensibility.md](Extensibility.md).

Iris’s lesson is semantic stages + compile-time rewrite + fallback, sitting on a renderer the framework controls. Anomaly’s rewrite sits on **Keen’s** renderer, because that renderer is the thing we cannot afford to clone.
