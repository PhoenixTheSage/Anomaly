# Anomaly Shader API

Extensible shader framework for Space Engineers 1. First product is a velocity buffer; the same compile hook must also support **additive injection** into Keen programs and **wholesale replacement** of named programs.

This is the architecture. Implementation order is [ROADMAP.md](ROADMAP.md). Product phases and Keen facts remain in [PLAN.md](PLAN.md). File-level Keen inventory is [KeenShaders.md](KeenShaders.md).

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
- Extra defines (`ANOMALY_VELOCITY`, later plugin-requested flags).
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

Fullscreen camera MV, future dilation, debug vis: **Anomaly shaders**, not Keen overlays. Consumers bind `IVelocityBuffer` by well-known type name ([ClientPlugin/Velocity/README.md](../ClientPlugin/Velocity/README.md)). Other plugins should rarely compile Keen permutations; they should consume textures Anomaly already bound.

Iris analog: `composite` / `final` — extra passes the framework owns.

---

## Public stage names

Do **not** generate 215 replace slots. Public surface is **semantic**:

| Stage name | Maps to Keen |
|------------|----------------|
| `GBuffer` | All opaque materials × GBuffer pass |
| `Depth` | Depth pass — **no extra MRT**; replacements must stay 0-target |
| `Forward` | Probe / far forward |
| `Transparent` | OIT |
| `Lighting.Dir` / `Lighting.Point` | Deferred lighting programs |
| `Post.Tonemap` / `Post.HBAO` | Named post files |
| `Anomaly.CameraVelocity` | Owned fullscreen |

Escape hatch: overlay by relative path for a one-off file.

Fallbacks: replacement missing or compile `#error` → Keen original. A Depth replacement that adds `SV_Target3` must fail compile, not ship.

---

## Composition (multi-plugin)

Iris packs are **exclusive**: one pack at a time. Pulsar loads **many plugins**. If SE-DLSS, a TAA plugin, and a “replace Standard” pack all patch `PixelStage.hlsli`, that is a merge conflict.

Rules:

1. **Anomaly owns extra GBuffer attachments.** Plugins request a slot (“RG16F velocity”) rather than splicing `SV_Target3` themselves.
2. **Defines are merged by Anomaly**, not by each Harmony patch on `MyShader`.
3. **Replace is exclusive per key**; inject is additive behind Anomaly-owned includes (e.g. `Anomaly/GBufferExtras.hlsli` that `#include`s registered snippets).
4. **Consumers do not Harmony-patch `DrawGameScene` or instance updates.** They bind registry textures. Anomaly draws / binds RTs.
5. **`ClearState` / DRS / device reset** stay Anomaly’s problem. Replacements must not leak RT/SRV ([Rich HUD](https://github.com/DarkHelmet/RichHudFramework)).

Injection + exclusive replace can coexist: velocity inject still applies to a replaced Standard pixel **only if** that pixel still includes `Passes/PixelStage.hlsli`. A full-file replace that omits the include opts out of extras — document that.

[SmoothFrames](https://github.com/WhiteFang34/SmoothFrames) also patches the render thread. Do not assume exclusive ownership of `DrawGameScene`.

---

## What this repo implements first

Keep the [PLAN.md](PLAN.md) cut:

1. Hook compile (include dir + `ANOMALY_VELOCITY` + Depth still compiles). That *is* the framework.
2. Ship velocity as **injection**, not as a Standard/Pixel fork.
3. Public shader API shape: Pulsar named assets + pack `Register` + named-stage replace table + buffer registry. Packs are Pulsar plugins that depend on Anomaly; see [ShaderPacks.md](ShaderPacks.md). Overlay replace is live (fail closed on conflict).

Iris’s lesson is semantic stages + compile-time rewrite + fallback, sitting on a renderer the framework controls. Anomaly’s rewrite sits on **Keen’s** renderer, because that renderer is the thing we cannot afford to clone.
