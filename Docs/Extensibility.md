# Extensibility roadmap

What to build **after velocity** on the compile hook. Architecture: [ShaderAPI.md](ShaderAPI.md). Velocity / hook slices: [ROADMAP.md](ROADMAP.md). Pack contract: [ShaderPacks.md](ShaderPacks.md). Keen inventory: [KeenShaders.md](KeenShaders.md).

**Now:** Layers 0–3 exist. Velocity is the first tenant. Slices **M–T**, **U–Z**, and **AA–AF** are in this repo: stage-scoped inject, pack defines, GBuffer attachments, lighting/GBuffer-read/atmosphere wraps, pass-begin bind registry, owned-pass scheduler, temporal policy, `FrameTemporal`, buffer catalog publish/lifetime, owned linear depth / Hi-Z / history / reactive mask, extra named stages, and **data-driven `Fullscreen/<Slot>` programs** (`FullscreenPassRegistry`).

**Next:** Slice **K** (sample pack) can demonstrate `Fullscreen/AfterAtmosphere/*.hlsl` + `passes[]`, lighting inject + velocity SRV, a C# AfterAtmosphere owned pass, or overlay `Decals` / `Shadows`.

---

## Docs map

| Doc | Role |
|-----|------|
| [ROADMAP.md](ROADMAP.md) | Velocity + hook slices A–L (done except Hub pin / sample pack) |
| [ShaderAPI.md](ShaderAPI.md) | Four layers; Iris comparison; composition rules |
| This file | Ordered work to generalize those layers beyond motion vectors (M–Z, AA–AF) |
| [ShaderPacks.md](ShaderPacks.md) | How a pack reaches Anomaly today |
| [PLAN.md](PLAN.md) | Why velocity; TAA / SSR named as later buffer products |
| [KeenShaders.md](KeenShaders.md) | Shared files worth wrapping vs 215 replace slots |

---

## Ground rules (do not skip)

Same as [ROADMAP.md](ROADMAP.md), plus:

- The compile intercept is the **only** compile door. Packs never Harmony-patch `MyShader`, `DrawGameScene`, `MyTransparentRendering.Render`, `MyAtmosphereRenderer`, or `MyToneMapping.Run`. Use `OwnedPassRegistry` or ship `Fullscreen/<Slot>/*.hlsl`. Anomaly owns `Draw(3)` for pack fullscreen programs.
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
| Include dirs | Anomaly `Shaders` + pack `Inject/` | — |
| Defines | `ANOMALY=1`; `ANOMALY_VELOCITY` + pack `defines` on GBuffer, lighting, and atmosphere | — |
| Overlay remap | `Overlay/<Stage>/…` or Keen-relative path | One owner per key (keep this) |
| Generated includes | `Anomaly/Extras/<Stage>.hlsli`; GBuffer alias; attachment fields; lighting/atmosphere extras | Lighting from Light wrap; Atmosphere from AtmosphereCommon wrap (`Keen/` prefix) |
| Pass-begin bind | GBuffer: velocity + extra attachment RTVs; Lighting/post: catalog SRVs + extras CB; Atmosphere: velocity **t6** (t5 is DensityLut) | — |
| Owned-pass slots | AfterLighting / AfterAtmosphere / AfterTransparent / BeforeTonemap / AfterTonemap / AfterUpscale | SE-DLSS calls `NotifyUpscaleComplete` |
| Fullscreen programs | `Fullscreen/<Slot>/*.hlsl` + `passes[]` → `FullscreenPassRegistry` | Anomaly compiles, binds t0–t3 / b6 / b7, merges, unbinds |
| Published buffer | `VelocityRegistry.Active`; `BufferCatalog.Active("velocity"|"linearDepth"|"hiZ"|"historyColor"|"reactiveMask"|"fullscreenIsolated")`; `Publish` / `RegisterLifetime`; `GBufferAttachments.TryGet` | Reserved names fail closed. Isolated outputs also publish `pass.<id>` |
| Stage probes | Sentinel compile per live named stage (overlays + injects) | Safety for overlays; not a product |

Velocity uses **layer 1** (GBuffer inject + `SV_Target3`) and **layer 3** (camera pass + registry). That pattern is the template. Do not add a second compile intercept.

---

## Composition (the actual API)

Pulsar loads many plugins. [ShaderAPI.md](ShaderAPI.md) composition rules stay the law; this roadmap is the work that **enforces** them with APIs:

1. Anomaly owns extra attachments; plugins request slots.
2. Defines are merged by Anomaly.
3. Replace is exclusive per key; inject is additive behind Anomaly includes.
4. Consumers bind registry textures, register `OwnedPassRegistry` draws, or ship `Fullscreen/` programs. They do not patch Keen pass methods or call `Draw`.
5. `ClearState` / DRS / device reset stay Anomaly’s problem (`RegisterLifetime` for pack RTs).
6. `exclusive: ["GBuffer"]` opts out of Anomaly-owned GBuffer stages (and velocity extras). Atmosphere wrap needs `["Atmosphere"]`.
7. Depth stays 3-attachment-free.
8. Atmosphere inject does not invent motion vectors. After `Scheduler.Done`, use `ContributeVelocity` / `Reactive`.

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

## Slice S — Owned Hi-Z / history color

Goal: layer 3 products that are **not** Keen permutations. Camera velocity is the template: Anomaly HLSL → Anomaly draw → publish → consumer binds.

- [x] Linear depth / Hi-Z pyramid from `ResolvedDepthStencil` (SSR, contact shadows). Full-res `linearDepth` (`R32_Float`, `compute_depth`); `hiZ` is a half-res 2×2 **min** (not `GenerateMips`)
- [x] Previous-frame color for TAA history (owned blit after post at `DrawGameScene` postfix — not a GBuffer MRT; first frame unpublished)
- [x] Jitter ownership: SE-DLSS owns jitter. Anomaly velocity is unjittered VP; linearize ignores M31/M32. Documented in [Buffers/README.md](../ClientPlugin/Buffers/README.md)
- [x] Debug vis of extras as Anomaly fullscreen (`CatalogDebug.hlsl` + settings **Debug buffer**), not Keen `Debug/*.hlsl`

**Slice S done when:** a named catalog texture is inspectable in-game and unbound after the pass.

---

## Slice T — More named stages

Slice J already maps GBuffer, Depth, Forward, Highlight, Transparent, lighting, and main post. Escape hatch: Keen-relative overlay.

| Stage | Keen root | Why |
|-------|-----------|-----|
| `Shadows` | `Shadows/Shadows.hlsl`, `Csm.hlsli` | Contact-hardening / different PCF |
| `Atmosphere` | `Transparent/Atmosphere/*` | Aerial perspective / LUT |
| `Decals` | `Decals/Decals.hlsl` | Extra-attachment coverage after main GBuffer |
| `GPUParticles` | `Transparent/GPUParticles/Render.hlsl` | Lit/streak extras |
| `EnvProbe` | `EnvProbe/*` | IBL prefilter tweaks |
| `Foliage` | `Foliage/*.hlsl` | Wind / coverage |
| `Anomaly.LinearDepth` | `LinearDepth.hlsl`, `HiZDownsample.hlsl` | Owned depth / Hi-Z overlay |
| `Anomaly.HistoryColor` | `HistoryCopy.hlsl` | Owned history blit overlay |

- [x] Map files in `ShaderStages`; unknown files under the new name fail closed
- [x] Sentinel compile in the probe table (slice L path)
- [x] Decals mapped. Anomaly’s `GBufferWrite` wrap always applies via include dir; `ANOMALY_VELOCITY` is **not** added for Decals/Foliage (no `RENDERING_PASS` on those compiles) — extra-attachment / velocity coverage on deferred decals is still a hole

**Slice T done when:** `Overlay/Decals/…` remaps and Show Status lists `stages=Decals`.

---

## Slice U — Owned-pass scheduler

Goal: visual plugins (Aurora-class) draw at named Keen moments without shipping Harmony. Anomaly owns the prefixes and the Rich HUD unbind.

Slots: `AfterLighting`, `AfterAtmosphere`, `AfterTransparent`, `BeforeTonemap`, `AfterTonemap`, `AfterUpscale`.

- [x] Well-known `OwnedPassRegistry.Register(id, slot, priority, temporalPolicy, draw)` (string/int reflection API + typed overload)
- [x] Harmony: prefix `Transparent.Render` / postfix after OIT; prefix+postfix `Atmosphere.RenderGBuffer` (unbind then AfterAtmosphere); `ToneMapping.Run` Last/First so AfterTonemap runs before SE-DLSS evaluate
- [x] `NotifyUpscaleComplete` after upscale evaluate; DrawGameScene postfix fallback if nobody notifies
- [x] Per-invocation `OwnedPassContext` (`Rc`, size, `LBuffer`, `ReactiveTarget`, `ContributeVelocity`)
- [x] Show Status: `Owned passes:`

**Do not** Harmony-patch `MyAtmosphereRenderer` from a pack. AfterAtmosphere runs after Anomaly unbinds extras so the tenant can set t20–t25.

**Slice U done when:** a pack can register AfterAtmosphere without a Harmony attribute.

---

## Slice V — Temporal policy

Goal: color-in / motion-out is explicit. Animated emission after scheduler Done is invisible to frozen MVs unless the pass opts in.

- [x] `TemporalPolicy` flags: `InColor`, `ContributeVelocity`, `Reactive`
- [x] Catalog `reactiveMask` (R8, cleared each frame when a Reactive pass runs)
- [x] `ContributeVelocity` composites extra MVs (mask &gt; 0.5) and republishes `velocity`
- [x] Debug buffer mode for the mask

SE-DLSS binding `reactiveMask` / calling `NotifyUpscaleComplete` lives in that repo. Anomaly only publishes the contract.

**Slice V done when:** an AfterAtmosphere pass can write reject pixels and overlay MVs without patching the velocity composite.

---

## Slice W — Frame temporal / jitter republish

Goal: one jitter owner (SE-DLSS). Anomaly reads `Projection.M31` / `M32` and republishes an unjittered VP on the extras CB.

- [x] `FrameTemporal` well-known type (`JitterX`/`JitterY`, `UnjitteredViewProj`, `PrevViewProj`, `InvalidateHistory`)
- [x] Same extras CB for lighting / atmosphere / post (append-only: frame index, jitter, two matrices)
- [x] Packs do not patch the projection

**Slice W done when:** lighting and atmosphere extras see the same unjittered matrices as owned passes.

---

## Slice X — Atmosphere wrap (not t5)

Goal: additive atmosphere HLSL without exclusive-replacing `AtmosphereGBuffer.hlsl`, and without stealing Keen `DensityLut` at t5.

- [x] Thin wrap of `Transparent/Atmosphere/AtmosphereCommon.hlsli` that `#include`s Keen via `Keen/` prefix
- [x] Compile intercept opens `Keen/…` from `Content/Shaders` and skips overlay remap
- [x] `#include <AnomalyAtmosphere.hlsli>` + `Anomaly/Extras/Atmosphere.hlsli` (`ANOMALY_ATMOSPHERE_STAGE`)
- [x] Bind velocity at **t6**; extras from t7; extras CB b6. Rebind per planet (`RenderOne`) because Keen `RenderEnd` clears t5–t6
- [x] Overlay of the wrap requires `exclusive: ["Atmosphere"]`
- [x] Empty `Anomaly/Extras/Atmosphere.hlsli` always generated; pack `defines` apply to `Transparent/Atmosphere/` compiles

**Do not** vendor a full copy of Keen AtmosphereCommon. **Do not** bind Anomaly extras at t5.

**Slice X done when:** `Inject/Atmosphere.hlsli` is visible to `AtmosphereGBuffer` and DensityLut still works.

---

## Slice Y — Catalog publish + lifetime

Goal: packs publish their own named textures (`aurora.noise`) without `OnDeviceReset` Harmony.

- [x] `BufferCatalog.Publish` / `Unpublish` / `UnpublishAll` by pack id
- [x] Reserved names (`velocity`, `linearDepth`, `hiZ`, `historyColor`, `reactiveMask`) fail closed
- [x] Same name from two pack ids fails closed
- [x] `RegisterLifetime` for DRS / device-end callbacks
- [x] `PublishedBuffer` helper (`ISharedBuffer`)

**Slice Y done when:** a pack can publish a texture and drop it on resize without patching `CreateScreenResources`.

---

## Slice Z — Frame-graph docs

Goal: the public story matches the real frame (velocity frozen at Scheduler.Done; transparent emission invisible to MVs; DLSS is LDR after tonemap; jitter owner is SE-DLSS).

- [x] [ShaderAPI.md](ShaderAPI.md) frame graph + owned-pass scheduler
- [x] This file (U–Z)
- [x] [ShaderPacks.md](ShaderPacks.md), [Buffers/README.md](../ClientPlugin/Buffers/README.md), [README.md](../README.md)
- [x] Shader developer wiki canvas

**Slice Z done when:** a pack author can read why Atmosphere inject does not fix DLSS ghosting.

---

## Slice AA — Ingest `Fullscreen/<Slot>` + `passes[]`

Goal: pack HLSL under `Fullscreen/` becomes a program spec. Overlay/Inject stay compile-time; this is **runtime compose**.

- [x] Scan `Fullscreen/<Slot>/<name>.hlsl` (skip `.hlsli`). Unknown slot fail closed
- [x] Defaults: `IsolatedAdd`, id `{packId}.{name}`, output `pass.{id}`, temporal `InColor`, priority from the pack
- [x] Parse `anomaly.json` `passes[]` (`id`, `slot`, `file`, `compose`, `priority`, `temporal`, `output`). Json overrides folder defaults
- [x] Hash fullscreen files into the pack fingerprint
- [x] `Apply` calls `FullscreenPassRegistry.ReplaceAll` for live packs only (rollback drops them)

**Slice AA done when:** a local pack’s PS is listed on Show Status `Fullscreen:` and Depth sentinels are unchanged.

---

## Slice AB — Dispatcher IsolatedAdd + Replace + owned scratch

Goal: Anomaly owns the draw. Packs do not create RTs or call `Draw`.

- [x] IsolatedAdd: pack PS → HDR scratch → additive merge into the slot dest
- [x] Replace: one owner per slot; two Replace claims fail closed; one Replace disables other compose on that slot
- [x] Dual scratch pairs (`ResolutionI` vs `ViewportResolution`) so AfterUpscale does not thrash HDR scratches
- [x] Data-driven programs run **before** C# `OwnedPassRegistry` callbacks
- [x] AfterTonemap postfix passes Keen’s `__result` as dest; HDR slots default to `LBuffer`

**Slice AB done when:** two IsolatedAdd AfterAtmosphere programs both merge; two Replace on the same slot both disable.

---

## Slice AC — Fixed bus + extras CB + unbind

Goal: packs stop inventing t20–t25 for Anomaly-drawn fullscreen.

- [x] t0 scene, t1 `linearDepth`, t2 `velocity`, t3 `reactiveMask`
- [x] b6 extras (same append-only layout as lighting: size, jitter, unjittered VP, prev VP, frame)
- [x] Shared `Fullscreen.hlsl` VS. Unbind SRVs/CBs/RTV before return (Rich HUD)
- [x] Merge copies dest to the other scratch first so dest is never sampled as an SRV while it is the RTV
- [x] Do not bind velocity at atmosphere t5

**Slice AC done when:** a pack PS can `#include <AnomalyFullscreen.hlsli>` and sample t0–t2 without `RequestSrv`.

---

## Slice AD — Debug + Status

Goal: name which tenant wrote a pixel.

- [x] Show Status `Fullscreen:` (`slot/compose:id`)
- [x] Debug buffer **FullscreenIsolated** (catalog `fullscreenIsolated`, last isolated output)
- [x] Reserved catalog name `fullscreenIsolated` (packs cannot `Publish` it)

**Slice AD done when:** Status lists programs and the debug overlay can show the last isolated RT.

---

## Slice AE — Temporal default + PublishOnly + PassUniforms

Goal: Aurora-class can be HLSL + json + a small uniform writer. DLSS contract is loud.

- [x] Folder default temporal is `InColor`. After `Scheduler.Done`, InColor without `Reactive` / `ContributeVelocity` logs once (`motion-out`)
- [x] `PublishOnly` draws isolated and skips merge (named `pass.<id>` still publishes)
- [x] `FullscreenPassRegistry.SetUniforms(id, float[])` writes b7 (`AnomalyPassUniform0–3`, 16 floats max)

**Slice AE done when:** a pack can drive intensity from C# without a private CB, and motion-out is visible in the log.

---

## Slice AF — Chain + IsolatedMix + DirectAdd

Goal: grades stack; veils can over-composite; cheap curtains stay opt-in.

- [x] Chain: each tenant samples the previous isolated (or scene); last copies to dest
- [x] IsolatedMix: `src + dest * (1 - src.a)`
- [x] DirectAdd: isolated then additive merge (same bus; dest is never the pack PS target)

**Slice AF done when:** two Chain programs ping-pong scratch and the last result lands in `LBuffer`.

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
| Pack Harmony on `DrawGameScene` / atmosphere / tonemap | `OwnedPassRegistry` + bind registry + catalog exist so they do not |
| Pack `MyPixelShaders.Create` / pack-owned fullscreen RTs | Anomaly owns `FullscreenPassRegistry` and the scratch pair |
| Last-writer-wins Replace on a slot | Same as silent Overlay overwrite — fail closed |
| Pack-chosen CB slot for uniforms | Anomaly allocates b7; `SetUniforms` is size-capped |
| Steal atmosphere t5 for AfterAtmosphere programs | `DensityLut` is Keen’s and already unbound |

---

## Suggested order

Do M before N if time is short: extras on PS unblocks additive work even with only `ANOMALY_VELOCITY`. **M–Z and AA–AF are implemented.**

| Order | Slice | Layer ([ShaderAPI.md](ShaderAPI.md)) | Depends on |
|------:|-------|--------------------------------------|------------|
| 1 | M stage-scoped inject + GBuffer PS extras | 1 | Hook (done) |
| 2 | N pack-requested defines | 0 | M (extras have `#ifdef`s) |
| 3 | O attachment slots | 1 | M, N |
| 4 | P Lighting / GBuffer-read wraps | 1 | M, O |
| 5 | Q pass-begin bind registry | 0+3 bind | P (lighting must sample) |
| 6 | R buffer catalog | 3 | Velocity registry (done) |
| 7 | S owned Hi-Z / history | 3 | R (done) |
| 8 | T more named stages | 2 | J (done) |
| 9 | U owned-pass scheduler | 3 | Q |
| 10 | V temporal policy | 3 | U, R |
| 11 | W FrameTemporal / extras CB | 1+3 | Q, S |
| 12 | X Atmosphere wrap (t6) | 1 | M, Q |
| 13 | Y catalog publish + lifetime | 3 | R |
| 14 | Z frame-graph docs | docs | U–Y |
| 15 | AA Fullscreen ingest | 3 | U, pack registry |
| 16 | AB IsolatedAdd / Replace dispatcher | 3 | AA, U |
| 17 | AC fixed bus + extras CB | 3 | AB, W |
| 18 | AD debug + Status | 3 | AB, S |
| 19 | AE PublishOnly + uniforms + motion-out | 3 | AB, V |
| 20 | AF Chain / IsolatedMix / DirectAdd | 3 | AB |

Slice K (sample pack) stays deferred. It can now demonstrate `Fullscreen/AfterAtmosphere` + `SetUniforms`, not only overlay.

---

## First implementation session

M–Z and AA–AF are in this repo. Next code slice is **K** (sample pack that ships `Fullscreen/AfterAtmosphere/*.hlsl`, injects Lighting extras, registers a C# AfterAtmosphere callback, or overlays `Decals`).
