# Extensibility roadmap

What to build **after velocity** on the compile hook. Architecture: [ShaderAPI.md](ShaderAPI.md). Velocity / hook slices: [ROADMAP.md](ROADMAP.md). Pack contract: [ShaderPacks.md](ShaderPacks.md). Keen inventory: [KeenShaders.md](KeenShaders.md).

**Now:** Layers 0–3 exist. Velocity is the first tenant. Slices **M–T**, **U–Z**, **AA–AN** are in this repo: stage-scoped inject, pack defines, GBuffer attachments, lighting/GBuffer-read/atmosphere wraps, pass-begin bind registry, owned-pass scheduler, temporal policy, `FrameTemporal` (including extras-CB sun, sky ambient, and planet air column), buffer catalog publish/lifetime, owned linear depth / Hi-Z / history / reactive mask, extra named stages, **data-driven `Fullscreen/<Slot>` programs** (`FullscreenPassRegistry`), the **color bus** (`hdrColor` / `upscaledColor` / `Display` / `ClaimUpscale`), the **256 B uniform bus** (`AnomalyPassUniform0–15`), **Slice AI** (`AnomalySunColor` / `AnomalyMarchSteps`), **Slice AJ** (`AnomalySkyAmbient` / `AnomalyVolumeAmbient`), **Slice AK** (`AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil`), **Slice AL** (local character mesh into `pointShadowAtlas`), **Slice AM** (`AnomalyVolumeNight` / `AnomalySunTransmittance`), and **Slice AN** (`volumeSunShadow` / `AnomalyVolumeSunShadow`).

**Next:** Slice **K** (sample pack) stays deferred. Pack workarounds that the next shader will also need are filed in [wiki/Framework-gaps.md](../wiki/Framework-gaps.md) in the same turn. Open: swapchain / post-CopyToRT consumers must UV-sample velocity and convert pixel delta by buffer size (`MatchesRenderResolution` is DRS, not DXGI).

---

## Docs map

| Doc | Role |
|-----|------|
| [ROADMAP.md](ROADMAP.md) | Velocity + hook slices A–L (done except Hub pin / sample pack) |
| [ShaderAPI.md](ShaderAPI.md) | Four layers; Iris comparison; composition rules |
| This file | Ordered work to generalize those layers beyond motion vectors (M–Z, AA–AJ) |
| [ShaderPacks.md](ShaderPacks.md) | How a pack reaches Anomaly today (assets + terminal pages) |
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
- Player-facing options go on `ClientPlugin.RichHud.TerminalConfigRegistry` under **Anomaly Shaders** (sibling of the **Anomaly** folder). Corner status goes on `HudOverlayRegistry`. Packs do not vendor Rich HUD.
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
| Owned-pass slots | AfterLighting / AfterAtmosphere / AfterTransparent / BeforeTonemap / AfterTonemap / AfterUpscale | Unique upscaler `ClaimUpscale` + `NotifyUpscaleComplete(rc, color)` |
| Fullscreen programs | `Fullscreen/<Slot>/*.hlsl` + `passes[]` → `FullscreenPassRegistry` | Anomaly compiles, binds t0–t3, HDR GBuffer t4–t6 or tonemap t4–t6, pack SRVs t7–t9 / b6 / b7 (64 floats, `AnomalyPassUniform0–15`), merges, unbinds. `SetEnabled` skips the draw. `SetScale` / `passes[].scale` sizes isolated RTs to 1 / 0.5 / 0.25. HDR slots skip LCD/TargetView. AfterUpscale t0 is `upscaledColor` when published |
| Published buffer | `VelocityRegistry.Active`; `BufferCatalog.Active("velocity"|"linearDepth"|"hiZ"|"historyColor"|"reactiveMask"|"fullscreenIsolated"|"hdrColor"|"litMips"|"upscaledColor")`; `Publish` / `RegisterLifetime`; `GBufferAttachments.TryGet` | Reserved names fail closed. Isolated outputs also publish `pass.<id>` |
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
| `SV_Target4–6` | Unused | Next full attachment (object id, linear depth) |
| `SV_Target7` | Internal diagnostics | Reserved for the same-draw velocity pipeline audit sideband |

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
| GBuffer begin | Live (velocity) | Extra MRT / VS t15–t16 / b6; developer `MrtWrite` also binds a dedicated immutable 16-byte probe at PS b7 and repeats the MRT/CB binds at draw boundaries; unbind after |
| Lighting draw | None | Bind extra GBuffer SRVs so Lighting extras actually sample |
| Post dispatch | None | Bind velocity / history / Hi-Z into Keen post, or skip Keen and run owned |
| OIT resolve | None | Bind extras for transparent resolve |
| `DrawGameScene` postfix | History swap + velocity debug | Owned composite (TAA, SSR) |
| `ClearState` / DRS / reset | Anomaly | Every extra RT follows `ResolutionI`; unbind |

- [x] Packs/plugins declare bind needs by named stage (`ShaderBindRegistry.RequestSrv`). Built-in: Lighting/post t5 ← catalog `"velocity"`; lighting t6+ ← live GBuffer color attachments
- [x] Anomaly owns the Harmony prefixes (lighting subpasses, tonemap, HBAO, OIT resolve) and the unbind
- [x] Geometry CB **b6** stays Anomaly’s uniform bus (jitter, frame index, temporal sample, pack scalars) — not a second per-plugin geometry CB
- [x] Geometry `MrtWrite` is runtime: VS b6 tests the velocity path and a dedicated 16-byte PS b7 payload tests the final Target3 output; fullscreen-program b7 is a separate pass scope
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
- [x] `NotifyUpscaleComplete(rc, color)` publishes catalog `upscaledColor` and binds AfterUpscale t0 / `ctx.SceneColor`
- [x] Per-invocation `OwnedPassContext` (`Rc`, size, `LBuffer`, `HdrColor`, `UpscaledColor`, `SceneColor`, `ReactiveTarget`, `ContributeVelocity`)
- [x] Show Status: `Owned passes:`

**Do not** Harmony-patch `MyAtmosphereRenderer` from a pack. AfterAtmosphere runs after Anomaly unbinds extras so the tenant can set t20–t25.

**Slice U done when:** a pack can register AfterAtmosphere without a Harmony attribute.

---

## Slice V — Temporal policy

Goal: color-in / motion-out is explicit. Animated emission after scheduler Done is invisible to frozen MVs unless the pass opts in.

- [x] `TemporalPolicy` flags: `InColor`, `ContributeVelocity`, `Reactive`, `Display`
- [x] Catalog `reactiveMask` (R8, cleared each frame when a Reactive pass runs). IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly with `Reactive` stamp dilated isolated luma.
- [x] IsolatedAdd `ContributeVelocity` reconstructs MVs from isolated.a; C# `ContributeVelocity` composites extra MVs (mask &gt; 0.5) and republishes `velocity`
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
- [x] Reserved names (`velocity`, `linearDepth`, `hiZ`, `historyColor`, `reactiveMask`, `fullscreenIsolated`, `hdrColor`, `upscaledColor`, `litMips`) fail closed
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
- [x] Parse `anomaly.json` `passes[]` (`id`, `slot`, `file`, `compose`, `priority`, `temporal`, `output`, `scale`). Json overrides folder defaults
- [x] `SetScale(id, scale)` / `TryGetOutputSize` — isolated RT at 1 / 0.5 / 0.25 (Replace ignores scale). Screen-space pixel radii use `AnomalySceneUvOffset` / `AnomalyInvSceneSize`, not pass size.
- [x] Hash fullscreen files into the pack fingerprint
- [x] `Apply` calls `FullscreenPassRegistry.ReplaceAll` for live packs only (rollback drops them)

**Slice AA done when:** a local pack’s PS is listed on Show Status `Fullscreen:` and Depth sentinels are unchanged.

---

## Slice AB — Dispatcher IsolatedAdd + Replace + owned scratch

Goal: Anomaly owns the draw. Packs do not create RTs or call `Draw`.

- [x] IsolatedAdd: pack PS → HDR scratch → additive merge into the slot dest
- [x] Replace: one owner per slot; two live Replace claims fail closed; a live Replace is the only compose drawn that frame (pack-disabled Replace does not fail-close Isolated siblings)
- [x] Dual scratch pairs (`ResolutionI` vs `ViewportResolution`) so AfterUpscale does not thrash HDR scratches
- [x] Data-driven programs run **before** C# `OwnedPassRegistry` callbacks
- [x] AfterTonemap postfix passes Keen’s `__result` as dest; HDR slots default to `LBuffer`

**Slice AB done when:** two IsolatedAdd AfterAtmosphere programs both merge; two Replace on the same slot both disable.

---

## Slice AC — Fixed bus + extras CB + unbind

Goal: packs stop inventing t20–t25 for Anomaly-drawn fullscreen.

- [x] t0 scene, t1 `linearDepth`, t2 `velocity`, t3 `reactiveMask`
- [x] HDR slots bind Keen GBuffer t4–t6; AfterTonemap / AfterUpscale bind `avgLuminance` / `bloom` / `dirt`
- [x] Pack catalog extras via `FullscreenPassRegistry.RequestSrv` / json `binds` at t7–t9
- [x] `FullscreenPassRegistry.SetEnabled(id, bool)` skips the draw without unregistering (pack checkbox; independent of Replace fail-closed)
- [x] Catalog `litMips` (GenerateMips of this-frame `LBuffer` at AfterLighting; request-driven)
- [x] HDR slots skip LCD / TargetView / TargetCamera (`MainViewGate`)
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
- [x] `FullscreenPassRegistry.SetUniforms(id, float[])` writes b7 (`AnomalyPassUniform0–15`, 64 floats / 256 B, same size as extras). `0–7` layout unchanged. Longer arrays fail closed; Anomaly logs once per program id.

**Slice AE done when:** a pack can drive intensity from C# without a private CB, and motion-out is visible in the log.

---

## Slice AF — Chain + IsolatedMix + DirectAdd

Goal: grades stack; veils can over-composite; cheap curtains stay opt-in.

- [x] Chain: each tenant samples the previous isolated (or scene); last copies to dest
- [x] IsolatedMix: `src + dest * (1 - src.a)`
- [x] DirectAdd: isolated then additive merge (same bus; dest is never the pack PS target)

**Slice AF done when:** two Chain programs ping-pong scratch and the last result lands in `LBuffer`.

---

## Slice AG — Color bus (HDR / upscale / display)

Goal: AfterUpscale is a **scheduler**. Display tenants (HdrRender-class BT.2390) and the unique upscaler (SE-DLSS) share one dest. They do not each Harmony-steal `MyToneMapping.Run` when both are live.

```
HDR LBuffer (internal)     catalog hdrColor
  BeforeTonemap (runs even if the upscaler later skips Run)
  skip Keen SDR if HasDisplayTenant (upscaler yields; Anomaly still returns a dest)
  DLSS evaluate HDR → output-sized dest
  NotifyUpscaleComplete(rc, dest)   catalog upscaledColor
  if Run returned null, Anomaly adopts dest so DrawGameScene can copy
  AfterUpscale, priority order:
      hdr.tonemap   Display — BT.2390 at ViewportResolution, read SceneColor
      smaa          optional filter
  HdrRender still owns swapchain / UI composite
```

- [x] Reserved catalog `hdrColor` (aliases `LBuffer`) and `upscaledColor` (notify dest, cleared each frame)
- [x] `NotifyUpscaleComplete(rc, color)` publishes dest, binds fullscreen t0, exposes `OwnedPassContext.SceneColor`
- [x] `TemporalPolicy.Display` + `HasDisplayTenant` + `ClaimUpscale` / `HasUpscaleConsumer` (one claimer, fail closed)
- [x] Fallback AfterUpscale still runs at native res with `LBuffer` when nobody notifies — no fake `upscaledColor`
- [x] Display without an upscaler skips Keen compute but returns a dest `DrawGameScene` can copy (fp16 wrap when Keen’s `BorrowCustom` is 8-bit UNORM); AfterUpscale grades `LBuffer` into it
- [x] Display with an upscaler: if `Run` was skipped without `__result`, Anomaly adopts the notified dest (or borrows the Display wrap) so `DrawGameScene` does not NRE on `.Linear` / `.SRgb`
- [x] Anomaly does not present

**Slice AG done when:** a Display tenant can Register AfterUpscale and sample the DLSS dest; DLSS can query `HasDisplayTenant` and publish that dest without a compile-time Anomaly reference.

---

## Slice AH — Uniform bus 256 B

Goal: Aurora-class packs stop packing scalars into derived constants. Still no pack-chosen CB slot.

- [x] `SetUniforms` accepts 64 floats (16×float4, 256 B — same size as extras)
- [x] `AnomalyPassUniform0–15` in `AnomalyFullscreen.hlsli`; `0–7` layout unchanged
- [x] Longer arrays fail closed; Anomaly logs once per program id

**Slice AH done when:** a pack can send 33–64 floats on b7 without a private CB, and a 65-float write is rejected in the Anomaly log.

---

## Slice AI — HDR illuminant + IsolatedMix energy + march helper

Goal: AfterAtmosphere volumes (clouds, future fog / godrays) light and cheapen without each pack inventing Keen `frame_` field layout, IsolatedMix multipliers, or step LOD. Compatibility-safe: append extras CB fields; helper in `AnomalyFullscreen.hlsli`. Do not steal atmosphere t5. Do not Harmony-patch `MyAtmosphereRenderer` for lighting. IsolatedMix stays over (`src + dest*(1-src.a)`); packs write **LBuffer energy**. No `IsolatedMixLit`.

Notation: [wiki/Framework-gaps.md](../wiki/Framework-gaps.md).

### Final Frontier celestial backgrounds (2026-09-18)

Sun-glare isolation added 2026-09-18: `SetNativeSunGlare(id, enabled)` controls
only the exact solar light after successful same-frame main replacement; no
world mutation. Defaults on; false supports diagnosing the reported orange
ring. User reports FSR motion-dependent star brightness; wider flux-preserving
profiles are a candidate mitigation, still requiring an in-game comparison.
Analytic effects, partial-disc visibility and full temporal validation remain open.


The initial [celestial contract](CelestialBackgrounds.md) is implemented and builds on both runtimes. FinalFrontier phase 1 uses it. Main/probe shader output passed offscreen D3D11 WARP checks. User validated replacement, brightness, zoom, sun sizing and day/night tracking; reflection refresh, lifecycle and upscaler validation remain pending.

- [x] Exclusive `CelestialBackgroundRegistry`: depth-masked BeforeFullscreen/AfterLighting main view and post-Keen probe background, preserving foreground lighting and main sky fog. Both shaders compile before activation; missing hook, conflict, failure or MSAA falls back to vanilla. No LightDir fork.
- [x] Copied 64-float uniforms and optional immutable float4 data (16 MiB cap), using host buffers and actual render contexts. Probe-time bind/unbind is explicit. Dedicated b6 view CB is 80 B; existing extras remain unchanged.
- [x] View-specific direction, pre-discard directionDx/directionDy derivatives for pixel-integrated stars, and derivative footprint; main scene projection unifies stars and sun. Offscreen tests cover no translation parallax and narrow-FOV finite output.
- [ ] Phase 1/3: validate actual infinity motion, jitter, FOV changes, probe refresh and upscalers in-game. Current depth-zero camera velocity is unmodified, not a proven defect. MSAA sample-aware background replacement remains open.
- [ ] Phase 4: sun-only glare ownership/restoration and partial-disc visibility. Initial disc preserves vanilla flare.
- [ ] Phase 5: optional projected labels through shared Rich HUD. Existing `HudOverlayRegistry` remains corner text only; shader guides must be excluded from probes.

Tenant design: `FinalFrontier/Docs/EnvironmentRenderingPlan.md`; validation: `FinalFrontier/Docs/IntegrationSpike.md`. Data indexing and optical polish remain later pack work. No changes to existing 320 B extras or shipped Slice AI behavior.

- [x] `AnomalySunColor` / `AnomalySunDiffuse` / `AnomalySunToward` / `AnomalySkyLuma` on b6 extras from `EnvironmentLight` (`PassExtrasCb.hlsli`, 288 B). `FrameTemporal.SunColor` / `SunToward` / `SunDiffuse` / `SkyLuma`. Fail closed when Environment is missing.
- [x] IsolatedMix `src.rgb` is **LBuffer energy**. Light with `AnomalySunColor * AnomalySunDiffuse`. `passes[].scale` is pixel cost, not lighting. No `IsolatedMixLit`.
- [x] `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)` in `AnomalyFullscreen.hlsli` — camera-to-volume × `AnomalySafetyScale`. Never per-ray `tMin`.
- [x] `AnomalySunVisibility(posCamRel, occluderCenterCamRel, occluderRadius, lightWrapMeters)` — local-up × `AnomalySunToward` with atmosphere-thickness wrap, **capped at 12% of occluder radius** (3-arg uses that 12% directly). Geometric AtmosphereRadius can be O(radius) and must not sun-light IsolatedMix night. Keen CSM is camera-local and does not cover a planet disk from orbit.
- [x] Pack SRV types: `#define ANOMALY_PACK_SRVn_TYPE Texture3D` before `#include <AnomalyFullscreen.hlsli>` (Volumetric Clouds).
- [x] AfterLighting LightPoint reconstruct: `AnomalyLightingUv` / `AnomalyLightingViewPos` / `AnomalyLightingN` / `AnomalyScreenUvToTexel` (HDR slots). IsolatedSub of tiled lights must not use interpolator `TEXCOORD` or skip `NdotL ≤ 0`. `maxTileLights` is the global stride. March the brightest N by energy (`sum L_i*(1-vis_i)`); do not clip the march at photometric `light.range`.
- [x] `AnomalyIgnWorld(world.xz)` for contact / SSGI dither that must not crawl with the camera. Unscaled seed. `AnomalyIgn(pixel)` follows the raster.
- [x] `AnomalyViewToLightingUv(viewPos)` inverse of live `compute_screen_ray` (BRDF). Contact: `AnomalyLightingViewPosUnjittered` + `AnomalyViewToDepthUv` (unjittered UV + `AnomalyLightingJitterUv`). Must not use `AnomalyUnjitteredViewProj` against jittered depth, must not scale step length with `AnomalySafetyScale`, and must not IsolatedSub a longer-range BRDF tail.
- [x] IsolatedSub dest units: `src.rgb` is a 0–1 dest fraction (`dest * (1-src)`). AfterLighting t0 is a dest copy when dest aliases LBuffer. IsolatedSub umbra `temporal` InColor+Reactive (not ContributeVelocity).
- [x] IsolatedSub dest-write: pack writes a 0–1 dest fraction (`AnomalyIsolatedSub` / `AnomalyIsolatedSubEnergy`); Anomaly blends `dest*(1-src)` onto dest (Replace dest RTV). IsolatedSub blits dest for t0 when dest aliases LBuffer. destHistory Dest[p] merge never reached Present. Reactive stamps IsolatedSub `.a`.
- [x] AfterLighting dest-alias t0 blit binds mergeCopy. DrawOne blits **before** the pack PixelShader and `DrawIsolated` rebinds `prog.Shader`. IsolatedSub with mergeCopy still bound copied dest onto dest (debug Replace skips the blit).
- [x] Scaled AfterFullscreen draw: `FullscreenPassRegistry.DrawFullscreen(rc, width, height)`. Keen `DrawFullscreenQuad()` with no viewport calls `SetScreenViewport()` and clips a half/quarter RT to the top-left of UV 0–1 (Prism.SSGI SVGF).

**Slice AI done when:** a new IsolatedMix volume can light from extras CB and cap steps with the helper without reading `frame_.Light` or guessing an HDR multiply. **Shipped.** Volumetric Clouds and Aurora are the first tenants. Screen Space Shadows is the IsolatedSub tenant.

## Slice AJ — night / sky illuminant

Keen `MyEnvironmentLightData` has no night-sky RGB. `AnomalySkyLuma` is Rec.709(`SunColorRaw`)×`AmbientDiffuseFactor` — a daylight proxy. Packs that hdr-lift it paint IsolatedMix night white.

- [x] `AnomalySkyAmbient` float3 on b6 extras (**304 B**). `FrameTemporal.SkyAmbient`. Unlifted `SunColorRaw * 0.028` only. Never `AmbientForwardPass` (Keen adds probe `LastAmbient` into that field, up to AmbientMaxClamp ≈ 0.3). Never AmbientDiffuse or pack HdrLift. Fail closed to zero when Environment is missing.
- [x] `AnomalyVolumeAmbient()` in `AnomalyFullscreen.hlsli` — returns extras ambient, capped at `AnomalySunColor * 0.028`. Independent of `AnomalySunVisibility`.

**Slice AJ done when:** IsolatedMix night volumes light from extras ambient without hdr-lifting SkyLuma. **Shipped.** Volumetric Clouds is the first tenant.

## Slice AK — planet air column / visual atmosphere ceiling

Geometric `AtmosphereRadius` (Bruneton mesh, often 1.75× average radius) sits outside Keen’s visual scattering limb. Gameplay air top is `AverageRadius + AtmosphereAltitude` (`LimitAltitude × MaxHillHeight`). AfterAtmosphere IsolatedMix already ignores the proxy’s depth (`DsvRo`). A `0.90 × AtmosphereRadius` deck pokes above the optical edge. Clipping the march at cloud inner / `AtmosphereRadius` is a spherical limb when the camera rises. Volumes may sit on terrain (`MinimumRadius`) if inner/outer are density, not occluders.

- [x] Append-only extras on b6 (**320 B**): `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil`. Radii are **meters from the planet center** (not camera-relative). Fail closed to 0. Game-thread `PlanetAtmosphere` samples the nearest `HasAtmosphere` / CloudLayers `MyPlanet`; `FrameTemporal` copies onto extras. Packs resolve `ClientPlugin.Shaders.PlanetAtmosphere.TryGetRadii` by name, or read the extras in HLSL (`AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade`).
- [x] Do not Harmony `MyAtmosphereRenderer`. Do not skip `Atmosphere_sphere.mwm`. `m_atmospheres` is private — do not reflect it.

**Slice AK done when:** IsolatedMix volumes clamp height to extras air-top / visual ceil without inventing `0.90 × AtmosphereRadius`. **Shipped.** `VisualCeil` equals `AirTop` today (optical IsolatedMix cap; future refine without changing air top). Volumetric Clouds is the first tenant: `BaseAltitude` 0–1 from `MinimumRadius` to that limb, CloudLayer height bands, GBuffer depth.

## Slice AL — local character mesh map

First-person GBuffer does not contain the body (camera sits in the head; near-plane clips everything but feet/arms when looking down). Contact that only marches `linearDepth` therefore cannot cast a walking-FP umbra. Occupancy is 2 m voxels. Cube AABBs are not a character mesh. Harmony-unhiding the first-person body fights Keen’s 1st-person materials. An extras capsule is a collision proxy, not a suit silhouette.

- [x] Light-space VS that writes world like `BoxDepth` (euclidean to the light). `MeshDepth` VS skins with `VertexTemplateBase` and interpolates camera-rel world; PS is `length(world-LightPos)` on FaceCb b0. Do not bind Keen DEPTH_ONLY VS (`z=max(z,0)`). IL from `DepthShaders`; Anomaly VS uses the same VERTEX_COMPONENTS / USE_SKINNING. Stamp one highest-detail drawable suit LOD; skip decals/glass/holo. Include camera-hidden opaque head/body proxies in the shadow view without changing main-view flags. Skip the local actor AABB on `RequestWorldBoxes`. `LocalCharacter` publishes actor id and `TryGetCamRelBox` — packs may exclude contact inside the render-thread caster bounds only when the matching atlas row has valid capture metadata. Do not Harmony-unhide the FP body.
- [x] Extras CB stays **320 B** (Slice AK). No capsule fields. Do not Harmony first-person hide. Do not treat occupancy / light-view AABBs as the body.

**Slice AL done when:** AfterLighting contact / SSGI can occlude with the local walking-FP body without inventing a pack-private player proxy. **Shipped.** Screen Space Shadows IsolatedSub mins `pointShadowAtlas` (mesh always when requested; AABB optional). Contact still owns GBuffer third-person. First-person head stays Keen-hidden.

### Point-shadow corrections and validation (2026-09-18)

`AnomalyPointShadows.hlsli` owns `AnomalyPointShadowFaceUv` and
`AnomalyPointShadowVisibility(atlas, row, directionWorld, distanceMetres, biasMetres)`.
The helper matches the producer's D3D viewport Y convention and performs 16 tent-weighted
depth comparisons clamped within the selected face (no cross-row or
header bleed). It is a reconstruction filter, not an area-light penumbra model;
seamless cross-face filtering remains future work. Light headers and face rows retain their layout; caster metadata occupies header texels 64 and 65.
`BufferCatalog.PointShadowStatus` reports selected/captured lights, cap, face size,
accepted mesh proxies, draw count and the last mesh state. Availability alone does
not prove that a caster was drawn.

`AnomalyLightingViewPosUnjittered` retains its name for compatibility but now uses
the live raster projection inverse. The depth at a jittered pixel cannot be
reconstructed by simply removing projection M31/M32; that displaces the surface.
Pair it with `AnomalyViewToDepthUv` to return to the original depth texel.

Validated: both target frameworks, HLSL compilation and a D3D11 WARP test of the
actual atlas helper against VRage matrices. First-person pose, head coverage,
view switching and shadow stability still require the in-game scene test in
ScreenSpaceShadows `Docs/ShadowValidation.md`.

Open Anomaly work: detailed world-mesh point-light casters (including alpha-cutout
materials), stable light IDs/selection shared with tiled lighting, per-light update
budgets and world-oriented cached faces. Current detailed mesh coverage is only
the controlled character; AABB extras are diagnostic approximations.

## Slice AM — planet-night IsolatedMix energy + optical sun transmittance

Slice AJ `AnomalySkyAmbient = SunColorRaw * 0.028` is **in-cloud day fill** (multiple-scatter when sun vis is low inside a sunlit deck). It is not the illuminant of the planet night hemisphere. IsolatedMix is `src + dest*(1-src.a)` onto AfterAtmosphere LBuffer; night dest is ~0, so 2.8% of HDR sun with high alpha is opaque headlights on the dark disk.

- [x] Confirm vis with pack-temp `CLOUD_DEBUG_SUN_VIS` (default 0) in Volumetric Clouds. Ships off. `SunLightDirection` is Keen’s direction *from* the sun; extras already store `-SunLightDirection`.
- [x] Helper `AnomalyVolumeNight(albedo, sunVis)` (3-arg destRgb overload ignored): night inscatter = AJ day fill × `AnomalyVolumeNightScale()` (0.05) so IsolatedMix matches Keen night ambient from orbit. Day fill stays AJ.
- [x] Helper `AnomalySunTransmittance(posCamRel, centerCamRel, planetR, airTop)`: deep night `geo=0`. Twilight is sample-height `sqrt(2h/r)` (floor air column, cap 0.40), symmetric `smoothstep(-t, t, μ)`, then `geo²` so IsolatedMix HDR does not wall. Do not return raw geo on `μ≤0` vs grazing OD on `μ>0`. Daytime 6-step OD only after geo is ~1. Do not use 12% Lambert wrap. Do not bind Keen CSM. 3-arg uses `AnomalyVolumeCeil()`.
- [x] No extras `AnomalyNightFloor` — helper scale is the floor. Extras stay **320 B**.
- [x] Volumetric Clouds rebases `CloudLitRadiance` (sky is pre-scaled). Aurora `NightAt` uses transmittance. Do not Harmony `MyAtmosphereRenderer`. Do not skip `Atmosphere_sphere.mwm`.

**Slice AM done when:** IsolatedMix night volumes match Keen night LBuffer from orbit (AJ × 0.05), not headlights or a grey deck on a black disk, and the terminator is a monotonic limb across Keen's twilight (not a bright band + hard cut at `μ=0`). **Shipped.** Volumetric Clouds is the first tenant.

## Slice AN — catalog sun-through-volume occlusion

Belly lighting inside an IsolatedMix volume does not darken terrain. AfterAtmosphere IsolatedMix is over; Keen CSM is camera-local and must not be bound from this slot. The next volume (aurora curtains, wet ground, SSGI) wants the same “sun through clouds” number.

- [x] Catalog product `volumeSunShadow` (2D). Anomaly owns the RT via `FullscreenPassRegistry` PublishOnly `output`. Packs must not `MyPixelShaders.Create` their own fullscreen target. Name is conventional, **not reserved** — `Publish` / `output` may use it.
- [x] Helper `AnomalyVolumeSunShadow(tex, samp, uv)` / `(remaining, alpha)`. `.r` is remaining sun (1 = none). `.a < 0.5` (missing or unbound SRV samples 0) fail closed to 1. Night / no-hit stamps write remain=1, a=1.
- [x] AfterAtmosphere IsolatedSub of dest (terrain + atmosphere already in LBuffer) uses `AnomalyIsolatedSubEnergy`. IsolatedMix volumes draw after the stamp (priority). Aurora / SSGI can sample later.
- [x] Extras stay **320 B**. Bind on catalog t7–t9 or an existing product slot. Do not Harmony lighting. Do not skip `Atmosphere_sphere.mwm`. Do not bind Keen CSM.

**Slice AN done when:** dest under a volume can receive sun occlusion without a pack-private RT or Keen cascade bind. **Shipped.** Volumetric Clouds stamps `volumeSunShadow` (weather OD along `AnomalySunToward`) then IsolatedSub dest; IsolatedMix follows.

## Slice AO — pack sparse volume / SVO catalog (open)

Planet-scale cloud SVOs (and any future aurora / dust brick field) need pack-owned **node pools** and **3D brick atlases** beyond the three Texture3D medium slots and camera froxels (≤8 km).

- [ ] Catalog publish for **StructuredBuffer** node pools (or documented Texture3D W×H×1 RGBA32F stand-in with Depth on `ISharedBuffer`).
- [ ] `ISharedBuffer` / PublishedBuffer expose **Depth** (and optionally Format) for 3D atlases — Width/Height alone is not enough for brick addressing docs.
- [ ] Optional pack **compute bake** path (UAV write into a catalog 3D atlas) so CPU Immutable recreate is not the only upload.
- [ ] Do **not** store planet SVO density inside Anomaly froxels (those stay shared near lighting).

**Workaround (Volumetric Clouds):** node atlas as Texture3D depth-1 RGBA32F + brick atlas R32F, published on `clouds.shape` / `clouds.detail` when active; CPU worker bake. Label `// GAP: Slice AO`.

**Slice AO done when:** a pack can Publish an octree node buffer + brick atlas without overloading shape/detail names, and Medium/Fullscreen can bind them by catalog name with known depth.

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
| Pack-chosen CB slot for uniforms | Anomaly allocates b7; `SetUniforms` is 64 floats / 256 B |
| Steal atmosphere t5 for AfterAtmosphere programs | `DensityLut` is Keen’s and already unbound |
| Pack-private IsolatedMix HDR scale / Keen `frame_.Light` lighting | Slice AI extras illuminant. `Frame.hlsli` layout is not a public contract |
| Per-pack march LOD that floors `AnomalySafetyScale` or uses per-ray `tMin` | Slice AI helper. Grazing chords and spectator slams TDR otherwise |
| AfterLighting IsolatedSub reconstruct from interpolator UV / first-N tile lights | LightPoint uses `screen_to_uv(SV_Position)`. Tile lists are unsorted; `maxTileLights` is the global stride. |
| AfterLighting IsolatedSub project with `AnomalyUnjitteredViewProj` / scale contact stepLen by `AnomalySafetyScale` / jittered reconstruct for the march | Unjittered view + `AnomalyViewToDepthUv`. SafetyScale changing step length jumps umbra when the camera looks. |
| AfterLighting IsolatedSub clip at photometric `light.range` / 20% lumaFloor / pixel IGN / longer-range BRDF tail | March past the Keen sphere (~1.4×). IsolatedSub photometric `sum L_i*(1-vis_i)` of the brightest N. Dither with `AnomalyIgnWorld`. |
| AfterLighting IsolatedSub dest-write via destHistory Dest[p] merge | IsolatedSub blends occupancy onto dest (same dest RTV as Replace). Pack writes `AnomalyIsolatedSub`. destHistory merge never reached Present. |
| AfterLighting dest-alias t0 blit leaving mergeCopy bound | Blit before the pack PixelShader. IsolatedSub with mergeCopy bound copies dest onto dest. |
| Sample Keen shadow cascades from AfterAtmosphere for a planet disk | Cascades are camera-local. `AnomalySunVisibility` uses local-up × sun with atmosphere light wrap |
| Use `AnomalySkyLuma * HdrLift` as night / ambient illuminant | Use `AnomalyVolumeAmbient()` / `AnomalySkyAmbient`. SkyLuma is sun luma × AmbientDiffuse |
| Scale `AnomalySkyAmbient` by `AmbientForwardPass` | Keen adds probe ambient into that field. Night IsolatedMix becomes sun-scale. Use `SunColor * 0.028` for **in-cloud day fill** only. |
| Treat `AnomalyVolumeAmbient` / 2.8% sun as planet-night illuminant | IsolatedMix over night dest≈0 is headlights. Slice AM: `AnomalyVolumeNight` = AJ × 0.05 + monotonic squared-limb transmittance. |
| Grow extras for volume-sun weights / bind Keen CSM from AfterAtmosphere | Slice AN: catalog `volumeSunShadow` + `AnomalyVolumeSunShadow`. Cascades are camera-local. Extras stay 320 B. |
| Pack `MyPixelShaders.Create` a sun-shadow RT | Anomaly owns PublishOnly scratch. IsolatedSub dest after the stamp. |
| Hard `μ≤0` / mid-chord vis for IsolatedMix night | Snaps clouds to night while Keen atmosphere is still day-lit. Per-sample `sqrt(2h/r)` twilight. |
| Return raw `geo` when `μ≤0` and `geo*exp(-OD)` when `μ>0` | Vis peaks on the night side of `μ=0` (bright band) then IsolatedMix HDR walls. Symmetric `smoothstep(-t, t)` then `geo²`; OD only after geo is ~1. |
| `smoothstep(-t, t*0.45, μ)` / cap twilight at 0.18 | vis=1 ~5° into day while Keen is still yellow twilight; 0.18 sat inside Pertam's geometric sunset. Cap 0.40. |
| Light IsolatedMix night from AfterAtmosphere dest luma | Night terrain dest≈0 vanishes the volume. Dest is not the grain — grazing vis misses are. |
| Gate planet night with a sphere-hit `tNear > 1` | Grazing disc noise + HashIgn IsolatedMix HDR sun at low alpha: white grain on dest (terrain included). Air-limb `sqrt(2h/r)` twilight, not a hard `μ≤0` cut. |
| Clip IsolatedMix at `AtmosphereRadius` / cloud inner sphere | Proxy does not write depth. Inner is density. GBuffer `AnomalyLinearDepth` (voxels/grids); planet body from orbit only. Height uses Slice AK extras. |
| AfterLighting IsolatedSub `occ = 1-minVis` / longer-range BRDF tail / `vis *= (1-hit)` every step | dest-punch at the falloff; stacked silhouettes (stairs). Photometric `AnomalyIsolatedSubEnergy`; first-hit then break. Softness is gather fraction. |
| Harmony-unhide first-person body / occupancy 2 m / extras capsule as the suit | GBuffer never contains the FP body. Slice AL mesh map in `pointShadowAtlas`. Occupancy is too coarse. Capsule is a collision proxy. |

---

## Suggested order

Do M before N if time is short: extras on PS unblocks additive work even with only `ANOMALY_VELOCITY`. **M–Z and AA–AN are implemented.**

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
| 21 | AG color bus (upscaledColor / Display) | 3 | U, R |
| 22 | AH uniform bus 256 B | 3 | AE |
| 23 | AI HDR illuminant / IsolatedMix energy / march helper | 3 | AC, AF, W |
| 24 | AJ night / sky illuminant | 3 | AI, W |
| 25 | AK planet air column / visual atmosphere ceiling | 3 | W, AC |
| 26 | AL local character mesh map | 3 | W, AC |
| 27 | AM planet-night IsolatedMix energy / optical sun transmittance | 3 | AJ, AK, AF |
| 28 | AN catalog sun-through-volume occlusion | 3 | AE, AF, AM |

Slice K (sample pack) stays deferred. It can now demonstrate `Fullscreen/AfterAtmosphere` + `SetUniforms`, not only overlay.

---

## First implementation session

M–Z and AA–AN are in this repo. Slice **K** (sample pack) stays deferred. When a pack invents a workaround the next shader will also need, file it on this page and [wiki/Framework-gaps.md](../wiki/Framework-gaps.md) in the same turn — do not wait for a dedicated roadmap pass.


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


### Camera stability and first-person scope (2026-09-18)

Extra local-character mesh capture is now first-person only. Third person uses
camera-depth contact shadows, with no off-screen third-person caster guarantee.
Atlas faces are world-axis aligned: headers remain view-space light positions;
sampling directions must be rotated from view to world coordinates.
AnomalyPointShadowVisibility uses a deterministic face-clamped 4x4 tent filter
(bilinear visibility convolved with [1,2,1]/4). Shadows reconstructs receivers
at SV_Position pixel centers, matching blocker sampling. GPU regressions pass;
live movement and perspective-switch acceptance remains pending.


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


### Shared atmospheric volumes (2026-09-18, opt-in acceptance build)

Anomaly now owns provider compilation, coefficient injection, spatial sun optical depth,
independent geometry shadows, room masks, joint integration/reconstruction/composition,
volume history and frame-valid products. Clouds has an atomic near/far adapter; Final
Frontier supplies fog and separate Atmosphere controls. FSR/DLSS receive shared reactive
coverage and FG consumes volume confidence while preserving floating-point color.
113 volume checks and 20 FG color/confidence checks pass. Geometry completeness,
cloud-only appearance, temporal stability and the 6 ms GPU target remain unverified in
live scenes. This is not a shipped extension. See Docs/SharedVolumetrics.md for the draft
contract, exact validation scope and mandatory acceptance checklist.


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
