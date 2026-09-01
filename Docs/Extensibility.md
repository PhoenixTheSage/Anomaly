# Extensibility roadmap

What to build **after velocity** on the compile hook. Architecture: [ShaderAPI.md](ShaderAPI.md). Velocity / hook slices: [ROADMAP.md](ROADMAP.md). Pack contract: [ShaderPacks.md](ShaderPacks.md). Keen inventory: [KeenShaders.md](KeenShaders.md).

**Now:** Layers 0–3 exist. Velocity is the first tenant. Slices **M–R** are in this repo: stage-scoped inject, pack defines, GBuffer attachments, lighting/GBuffer-read wraps, pass-begin bind registry, buffer catalog.

**Next:** Slice **S** when a real consumer needs owned Hi-Z / history color. Slice **K** (sample Hidden pack) can now demonstrate lighting inject + velocity SRV.

---

## Docs map

| Doc | Role |
|-----|------|
| [ROADMAP.md](ROADMAP.md) | Velocity + hook slices A–L (done except Hub pin / sample pack) |
| [ShaderAPI.md](ShaderAPI.md) | Four layers; Iris comparison; composition rules |
| This file | Ordered work to generalize those layers beyond motion vectors |
| [ShaderPacks.md](ShaderPacks.md) | How a pack reaches Anomaly today |
| [PLAN.md](PLAN.md) | Why velocity; TAA / SSR named as later buffer products |
| [KeenShaders.md](KeenShaders.md) | Shared files worth wrapping vs 215 replace slots |

---

## Ground rules (do not skip)

Same as [ROADMAP.md](ROADMAP.md), plus:

- The compile intercept is the **only** compile door. Packs never Harmony-patch `MyShader` / `DrawGameScene`.
- Do not fork `Materials/*`. Inject shared stages; replace is exclusive per key.
- Do not inject into Depth or `VertexTemplateBase` / `PixelTemplateBase` (those are shared with Depth).
- Anomaly owns extra GBuffer attachments. Plugins **request a slot**; they do not splice `SV_Target3`.
- Defines are merged by Anomaly, not by each pack’s Harmony prefix.
- Consumers bind registry textures by well-known type name. No compile-time reference to Anomaly.
- Unbind extra RT/SRV before returning to Keen (Rich HUD). Extra RTs follow `ResolutionI` / DRS / device reset.
- SmoothFrames may also patch the render thread; do not assume exclusive `DrawGameScene`.
- Do not clone Keen’s renderer (Iris-style full pipeline swap). Atmosphere, CSM, particles stay Keen unless a pack **exclusively** overlays those named stages.

---

## What the hook already is

Every Keen permutation already goes through `MyShaderCompiler` (`ShaderCompileIntercept` / `ShaderPackRegistry`):

| Mechanism | Live today | Gap |
|-----------|------------|-----|
| Include dirs | Anomaly `Shaders` + pack `Inject/` | Lighting/post still need Anomaly wraps (slice P) |
| Defines | `ANOMALY=1`; `ANOMALY_VELOCITY` + pack `defines` on GBuffer and lighting | — |
| Overlay remap | `Overlay/<Stage>/…` or Keen-relative path | One owner per key (keep this) |
| Generated includes | `Anomaly/Extras/<Stage>.hlsli`; GBuffer alias; attachment fields; lighting/read SRVs | Lighting extras included from Light wrap |
| Pass-begin bind | GBuffer: velocity + extra attachment RTVs; Lighting/post: catalog SRVs + extras CB | Owned Hi-Z / history (slice S) |
| Published buffer | `VelocityRegistry.Active`; `BufferCatalog.Active("velocity")`; `GBufferAttachments.TryGet` | `linearDepth` / history when a producer exists |
| Stage probes | Sentinel compile per live named stage (overlays + injects) | Safety for overlays; not a product |

Velocity uses **layer 1** (GBuffer inject + `SV_Target3`) and **layer 3** (camera pass + registry). That pattern is the template. Do not add a second compile intercept.

---

## Composition (the actual API)

Pulsar loads many plugins. [ShaderAPI.md](ShaderAPI.md) composition rules stay the law; this roadmap is the work that **enforces** them with APIs:

1. Anomaly owns extra attachments; plugins request slots.
2. Defines are merged by Anomaly.
3. Replace is exclusive per key; inject is additive behind Anomaly includes.
4. Consumers bind registry textures.
5. `ClearState` / DRS / device reset stay Anomaly’s problem.
6. `exclusive: ["GBuffer"]` opts out of Anomaly-owned GBuffer stages (and velocity extras).
7. Depth stays 3-attachment-free.

A TAA plugin + SE-DLSS + a tonemap pack coexist if TAA **injects** and **binds**, the tonemap pack **replaces** `Post.Tonemap` only, and both read velocity from the registry.

---

## Slice M — Stage-scoped inject + extras on GBuffer PS

Goal: additive HLSL besides velocity, without exclusive replace. Pack `Inject/` is not one VS-only blob.

**Stuck today:** every inject file concatenates into `Anomaly/GBufferExtras.hlsli`, included from `Anomaly.hlsli`, which GBuffer **vertex** stage pulls in. Pixel stage never sees pack injects unless the pack overlays `PixelStage` / `GBufferWrite`.

- [x] Generate per-stage extras: `Anomaly/Extras/GBuffer.hlsli` (keep `GBufferExtras.hlsli` as an alias or include)
- [x] Pack layout: `Inject/GBuffer.hlsli`, later `Inject/Lighting.hlsli`, `Inject/Post.Tonemap.hlsli` — same `Inject/` folder, keyed by [named stage](ShaderAPI.md)
- [x] Unknown `Inject/<not-a-stage>/` fails closed (same as unknown overlay under a stage name)
- [x] Include extras from GBuffer **PixelStage** / `GBufferWrite.hlsli`, not only VS
- [x] Unscoped `Inject/*.hlsli` (no stage folder) still concatenates into GBuffer extras for v1 packs
- [x] Fingerprint still hashes inject text so Keen’s preprocess cache misses

**Slice M done when:** a local pack can add a GBuffer PS helper without overlaying `PixelStage.hlsli`, and Depth still compiles.

---

## Slice N — Pack-requested defines

Goal: Anomaly merges permutation macros. Packs do not patch `GlobalShaderMacros`.

`anomaly.json`:

```json
{
  "id": "example.objectid",
  "defines": ["ANOMALY_OBJECTID"]
}
```

- [x] Parse `defines` (string array). Empty / missing = none
- [x] Merge onto GBuffer permutations the same way as `ANOMALY_VELOCITY` (never Depth, never `DEPTH_ONLY`)
- [x] Same define from two packs is fine (additive). Overlay key conflict still fail-closed
- [x] Fingerprint includes the merged define set
- [x] Show Status: live defines (short list)
- [x] Reserved: `ANOMALY`, `ANOMALY_VELOCITY`, `RENDERING_PASS`, `DEPTH_ONLY` — packs cannot redefine Keen / Anomaly core macros

**Slice N done when:** a local pack’s `#ifdef ANOMALY_OBJECTID` compiles on GBuffer and is absent from Depth.

---

## Slice O — Attachment slot allocator

Goal: `SV_Target3` stays velocity. Next extras request a slot; they do not splice Keen’s `GbufferOutput`.

D3D11 room vs bandwidth:

| Slot | Status | Next use |
|------|--------|----------|
| `SV_Target0–2` | Keen | Do not repack unless `exclusive: ["GBuffer"]` |
| `GBuffer1.a` | Unused | Cheap packed extra (id / flags) — no new RT |
| `SV_Target3` | Velocity | Keep; packs cannot claim it |
| `SV_Target4+` | Unused | Next full attachment (object id, linear depth) |

- [x] Well-known request API (static type, no compile-time reference), e.g. `RequestAttachment("objectid", format, stage: GBuffer)`
- [x] Anomaly assigns `SV_TargetN` or a packed channel; generated extras declare the struct field
- [x] `GBufferVelocity`-style bind grows from “always four RTVs” to “N RTVs for live attachments”
- [x] Depth permutations never see extra targets
- [x] Conflict: two packs requesting the same name share the slot; two packs requesting incompatible formats for the same name fail closed
- [x] Unbind every extra RT after GBuffer (Rich HUD)

Natural Buffer-API products after velocity ([PLAN.md](PLAN.md) already names TAA / SSR inputs): object / actor id, linear depth, packed flags in `GBuffer1.a`. Previous color is usually an **owned pass** (slice S), not a GBuffer MRT.

**Slice O done when:** velocity still owns Target3; a second attachment can be requested and bound without editing `GBufferWrite.hlsli` by hand.

---

## Slice P — Thin wraps on GBuffer read + Lighting

Goal: lighting can sample extras without exclusive-replacing `LightDir.hlsl`. Same trick as GBuffer stages: Anomaly overlays the **shared include**, then `#include`s extras.

Do this only after M (extras files exist) and preferably O (something to sample).

| Shared file | Why wrap | Unlocks |
|-------------|----------|---------|
| `GBuffer/GBuffer.hlsli` + `Surface.hlsli` | Deferred **read** of extra attachments | Lighting / SSR see velocity, id, extra AO |
| `Lighting/Light.hlsli` | Dir / point / spot all include it | Additive lighting without replacing `LightDir.hlsl` |

- [x] Anomaly-owned overlays of those includes (not a Materials fork)
- [x] `#include <Anomaly/Extras/Lighting.hlsli>` from the Light wrap
- [x] Overlay of Anomaly-owned lighting includes requires `exclusive: ["Lighting"]` (not Lighting.Dir / .Point / .Spot). GBuffer **read** wraps accept `exclusive: ["GBuffer"]` or `["Lighting"]`. Write stages still need `["GBuffer"]`. Documented in [ShaderPacks.md](ShaderPacks.md)
- [x] Stage probes: existing Lighting sentinels still compile; Lighting inject maps to Dir/Point/Spot probes

**Do not** wrap `VertexTemplateBase` / `PixelTemplateBase`. **Do not** require wrapping `EnvAmbient.hlsli` / `Fog.hlsli` until a pack needs additive IBL/fog.

**Slice P done when:** a pack `Inject/Lighting.hlsli` is visible to `LightDir` without overlaying `Lighting/LightDir.hlsl`.

---

## Slice Q — Pass-begin bind registry

Goal: compile intercept is not enough. Overlays that need extra SRVs/CBs outside GBuffer must not ship their own Harmony.

| Keen moment | Today | Bind registry |
|-------------|-------|----------------|
| GBuffer begin | Live (velocity) | Extra MRT / t15–t16 / b6; unbind after |
| Lighting draw | None | Bind extra GBuffer SRVs so Lighting extras actually sample |
| Post dispatch | None | Bind velocity / history / Hi-Z into Keen post, or skip Keen and run owned |
| OIT resolve | None | Bind extras for transparent resolve |
| `DrawGameScene` postfix | History swap + velocity debug | Owned composite (TAA, SSR) |
| `ClearState` / DRS / reset | Anomaly | Every extra RT follows `ResolutionI`; unbind |

- [x] Packs/plugins declare bind needs by named stage (`ShaderBindRegistry.RequestSrv`). Built-in: Lighting/post t5 ← catalog `"velocity"`; lighting t6+ ← live GBuffer color attachments
- [x] Anomaly owns the Harmony prefixes (lighting subpasses, tonemap, HBAO, OIT resolve) and the unbind
- [x] Geometry CB **b6** stays Anomaly’s uniform bus (jitter, frame index, temporal sample, pack scalars) — not a second per-plugin geometry CB
- [x] Lighting / post use **different** slot maps; they get a separate extras CB (`Anomaly.LightingExtrasCB` at b6), not the geometry velocity CB
- [x] Show Status: which stages have extra binds this frame (`Pass binds:`)

**Slice Q done when:** a lighting extras inject can sample the velocity SRV without the pack patching `MyGBufferPass`.

---

## Slice R — Buffer catalog

Goal: `IVelocityBuffer` is the first published texture, not the only one. Same discovery pattern ([ClientPlugin/Velocity/README.md](../ClientPlugin/Velocity/README.md)).

- [x] Catalog keyed by well-known name (`velocity`, later `linearDepth` / `hiZ` / `objectId` / `historyColor`)
- [x] `IVelocityBuffer` / `VelocityRegistry` stay as typed convenience; catalog `Active("velocity")` aliases the same producer
- [x] Consumers resolve by type name; no compile-time reference
- [x] Motion blur / TAA plugins consume the catalog; they do not compile Keen permutations or generate a second MV buffer
- [x] Document names + formats + convention in [ClientPlugin/Buffers/README.md](../ClientPlugin/Buffers/README.md)

**Slice R done when:** SE-DLSS can keep binding `VelocityRegistry.Active`, and a second consumer can bind `linearDepth` the same way once a producer exists.

---

## Slice S — Owned Hi-Z / history color (when a consumer exists)

Goal: layer 3 products that are **not** Keen permutations. Camera velocity is the template: Anomaly HLSL → Anomaly draw → publish → consumer binds.

Do **not** start this until a real consumer needs it (TAA or SSR in another repo, same relationship as SE-DLSS).

- [ ] Linear depth / Hi-Z pyramid from `ResolvedDepthStencil` (SSR, contact shadows)
- [ ] Previous-frame color for TAA history (owned pass after lighting, before HUD — not a GBuffer MRT)
- [ ] Jitter ownership: today SE-DLSS owns jitter. TAA as an Anomaly pass needs Anomaly-owned unjittered VP (already true for velocity) plus a documented jitter contract with SE-DLSS
- [ ] Debug vis of extras as Anomaly fullscreen (velocity debug overlay is the pattern), not Keen `Debug/*.hlsl`

**Slice S done when:** a named catalog texture is inspectable in-game and unbound after the pass.

---

## Slice T — More named stages (on demand)

Slice J already maps GBuffer, Depth, Forward, Highlight, Transparent, lighting, and main post. Escape hatch: Keen-relative overlay.

Add stage names when a **real pack** needs them, not all 215 files:

| Stage | Keen root | Why |
|-------|-----------|-----|
| `Shadows` | `Shadows/Shadows.hlsl`, `Csm.hlsli` | Contact-hardening / different PCF |
| `Atmosphere` | `Transparent/Atmosphere/*` | Aerial perspective / LUT |
| `Decals` | `Decals/Decals.hlsl` | Extra-attachment coverage after main GBuffer |
| `GPUParticles` | `Transparent/GPUParticles/Render.hlsl` | Lit/streak extras |
| `EnvProbe` | `EnvProbe/*` | IBL prefilter tweaks |
| `Foliage` | `Foliage/*.hlsl` | Wind / coverage |

- [ ] Map files in `ShaderStages`; unknown files under the new name fail closed
- [ ] Sentinel compile in the probe table (slice L path)
- [ ] Decals first if extras (slice O) ship — `Decals.hlsl` writes GBuffer after the main pass (velocity coverage hole)

**Slice T done when:** `Overlay/Decals/…` (or the first demanded name) remaps and Show Status lists `stages=Decals`.

---

## What not to add

| Idea | Why not |
|------|---------|
| Second Harmony compile hook | Anomaly owns `MyShaderCompiler`; two intercepts fight |
| Workshop / `.sbc` packs | Different loader; no Pulsar `DependencyIds` / asset SHA-256 |
| Fork `Materials/Standard/Pixel.hlsl` for extras | 215-file tax; inject shared stages instead |
| Inject into Depth or template bases | Fourth target / Depth cache / shadows |
| Widen Keen’s 64-byte instance VB | Shared with depth packing |
| ReShade Present hook for geometry buffers | Cannot see object velocity or GBuffer extras |
| Iris full renderer swap | Reimplement Keen’s deferred engine |
| Pack Harmony on `DrawGameScene` | Bind registry + catalog exist so they do not |

---

## Suggested order

Do M before N if time is short: extras on PS unblocks additive work even with only `ANOMALY_VELOCITY`. Do not start S/T without a consumer or a pack that needs the stage. **M–R are implemented.**

| Order | Slice | Layer ([ShaderAPI.md](ShaderAPI.md)) | Depends on |
|------:|-------|--------------------------------------|------------|
| 1 | M stage-scoped inject + GBuffer PS extras | 1 | Hook (done) |
| 2 | N pack-requested defines | 0 | M (extras have `#ifdef`s) |
| 3 | O attachment slots | 1 | M, N |
| 4 | P Lighting / GBuffer-read wraps | 1 | M, O |
| 5 | Q pass-begin bind registry | 0+3 bind | P (lighting must sample) |
| 6 | R buffer catalog | 3 | Velocity registry (done) |
| 7 | S owned Hi-Z / history | 3 | R + a real consumer |
| 8 | T more named stages | 2 | A pack that needs the name |

Slice K (sample Hidden pack) stays deferred until M/N exist so the sample can demonstrate inject + defines, not only overlay.

---

## First implementation session

M–R are in this repo. Next code slice is **S** (owned Hi-Z / history) when a consumer exists, or **K** (sample pack that injects Lighting extras and samples `AnomalyVelocityBuffer`).
