# Extensibility roadmap

What to build **after velocity** on the compile hook. Architecture: [ShaderAPI.md](ShaderAPI.md). Velocity / hook slices: [ROADMAP.md](ROADMAP.md). Pack contract: [ShaderPacks.md](ShaderPacks.md). Keen inventory: [KeenShaders.md](KeenShaders.md).

**Now:** Layers 0–3 exist. Velocity is the first tenant. Slices **M–T**, **U–Z**, **AA–AN** are in this repo: stage-scoped inject, pack defines, GBuffer attachments, lighting/GBuffer-read/atmosphere wraps, pass-begin bind registry, owned-pass scheduler, temporal policy, `FrameTemporal` (including extras-CB sun, sky ambient, and planet air column), buffer catalog publish/lifetime, owned linear depth / Hi-Z / history / reactive mask, extra named stages, **data-driven `Fullscreen/<Slot>` programs** (`FullscreenPassRegistry`), the **color bus** (`hdrColor` / `upscaledColor` / `Display` / `ClaimUpscale`), the **256 B uniform bus** (`AnomalyPassUniform0–15`), **Slice AI** (`AnomalySunColor` / `AnomalyMarchSteps`), **Slice AJ** (`AnomalySkyAmbient` / `AnomalyVolumeAmbient`), **Slice AK** (`AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil`), **Slice AL** (local character mesh into `pointShadowAtlas`), **Slice AM** (`AnomalyVolumeNight` / `AnomalySunTransmittance`), **Slice AN** (`volumeSunShadow` / `AnomalyVolumeSunShadow`), **Slice AR** (`AnomalyIsForeground`), **Slice AS** (`AnomalyVolumeSkipFloor` / `AnomalyVolumeSkipDt` / `AnomalyVolumeAdvance`), and **Slice AT** (`AnomalyPlanetShellUv` / `AnomalyPlanetSamplePos`; second shell tenant still open).

**Next:** Slice **K** (sample pack) stays deferred. Pack workarounds that the next shader will also need are filed in [wiki/Framework-gaps.md](../wiki/Framework-gaps.md) in the same turn. Open: swapchain / post-CopyToRT consumers must UV-sample velocity and convert pixel delta by buffer size (`MatchesRenderResolution` is DRS, not DXGI).

---

## Docs map

DX11 pack compatibility (2026-10-14): source-cache companion discovery follows Pulsar's sanitized filenames, CPU catalog uploads fulfill Keen's ISrvBindable shader contract, and the shared facade preserves pending lifetime registrations. These are host fixes shared by packs, not tenant-specific upload workarounds. Dual-runtime hardware readback and source identity checks pass; live SSGI/Aurora visibility remains pending. See Tests/Dx11PackCompatibility and wiki/Framework-gaps.md.

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

Shared HUD parity (2026-10-14, Slice AI): terminal pages, corner overlays, and interactive windows now live in Anomaly's renderer-independent core on DX11 and Vulkan. Consumers queue a separate Status page independently of settings/device initialization. DX11 binds its settings callbacks and persistence to that one shared registry through the live Pulsar core identity. Legacy/Interim registration and source-name rebinding checks pass; visible in-world Master acceptance remains pending. See [TerminalConfig.md](TerminalConfig.md).

Interactive map/window tenant (2026-10-02, Slice AI shared HUD capability):
`ClientPlugin.RichHud.InteractiveOverlayRegistry` API v1 now hosts resizable
modal RHUD windows, standard controls, viewport input/resize, projected geometry
batches and virtualized rows. Final Frontier supplies sanitized world snapshots;
the pack does not vendor RHUD or patch rendering. Corner text remains on
`HudOverlayRegistry`. See [the interactive overlay contract](InteractiveOverlays.md).
Both runtime builds and headless contract tests pass; in-game input/visual/GPU
acceptance is pending.

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
| Fullscreen programs | `Fullscreen/<Slot>/*.hlsl` + `passes[]` → `FullscreenPassRegistry` | Anomaly compiles, binds t0–t3, HDR GBuffer t4–t6 or tonemap t4–t6, pack SRVs t7–t9 and t12–t13 / tiled lights t10–t11 / b6 / b7 (64 floats, `AnomalyPassUniform0–15`), merges, unbinds. `SetEnabled` skips the draw. `SetScale` / `passes[].scale` sizes isolated RTs to 1 / 0.5 / 0.25. HDR slots skip LCD/TargetView. AfterUpscale t0 is `upscaledColor` when published |
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
- [x] Pack catalog extras via `FullscreenPassRegistry.RequestSrv` / json `binds` at t7–t9 and t12–t13 (t10/t11 stay tiled lights)
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

Environment-family continuation (2026-10-22, implementation in progress): shared
native generations now cover atmosphere LUT/main-view/probe and foliage's
VS/GS/PS dependency closure under new bits24/25. Source capture, stage injection,
reserved bindings, atomic activation and completion-based retirement stay in the
existing Anomaly/SE-Vulkan relationship. Probe/foliage GPU qualification is pending;
no pack-specific compiler or renderer host was added. See wiki/Named-stages.md.


Shared host follow-up (2026-10-22): optional fullscreen catalog SRVs now have
true-null dimensions, reads and sampling on Vulkan under native bit23 and device
nullDescriptor support. The core owns resource-type checks and actionable
older-host refusal; packs do not supply dummy images or branch on Vulkan internals.
Query ShaderPackRegistry.GetCapabilities().ClassicNullResourcesSupported.
Structured uploads remain distinct from typed-buffer views; extended volume/MRT
inputs remain required. See wiki/Buffer-catalog.md for execution evidence and
coordinated Anomaly/SE-Vulkan requirements. This does not close other stage6
owners/attachments, stage7 or the remaining automated ecosystem acceptance.


Vulkan guest host follow-up (2026-10-16): startup compilation now waits for the native-bound frame callback, retries renderer availability and late registrations, and uses the renderer's driver SPIR-V preparation. The host supplies manifest binds/priority, slot macros, pack/stock includes, the exact stock frame CB, and native point-light/tile buffers. Owned depth/history/Hi-Z catalog routing and scene dimensions are corrected. Compiler/pipeline failures and submitted counts are explicit. See `Docs/Vulkan.md` and `Tests/GuestRuntime`. In-world visual parity, Vulkan occupancy/pointShadowAtlas/litMips and the volumeReactive producer remain open shared capabilities; do not duplicate them inside packs.

### Display colour-mode signal (2026-10-02, open; Final Frontier workaround)

Final Frontier needs to distinguish SDR's channel-compressing filmic shoulder
from HDR Output's luminance-preserving shoulder when authoring solar gold.
`HasDisplayTenant` is an ownership signal, not an HDR colour-space declaration;
assembly presence and pack registration do not establish a live display shader.
Anomaly currently has no structured active display colour-mode contract.

Proposed shared capability: a display tenant declares its output encoding and
the host exposes that mode only while the tenant is ready and enabled; absence,
compile failure, conflict and unload restore the SDR default. Make it readable
by reflection or append it to view metadata without changing existing bindings.
Do not infer HDR from a generic `Display` policy or patch Keen lighting in packs.

Final Frontier temporarily queries the public
`FullscreenPassRegistry.TryGetProgramStatus("hdr.tonemap", out status)` and
selects HDR gold only for `live ` status. This is labeled `GAP: Slice AI` in
`AnomalyBridge.ReadHdrDisplay`: it depends on HDR Output's program id and status
prefix, and cannot identify other future HDR display tenants. Mode transitions
refresh celestial uniforms even when user settings and altitude are unchanged.
The shader correction preserves solar luminance and leaves SDR surface colours
intact. The later unified glow changes the composite in both display modes.
Eighty-seven GPU checks (including the actual HDR display shader at three display
peaks) and eighty-four plugin integration checks pass; HDR colour and unified
glow still require in-game acceptance. The display-mode metadata API remains open.

### SSGI gather and temporal evidence (2026-10-01, implemented in tenant)

Prism.SSGI corrected the Slice AR coverage contract: accepted 6/9 and 7/9 samples receive at least one SSILVB angular sector even when their depth thickness is zero. Keep thickness extrusion restricted to counts >= 8; an accepted illuminant must not disappear solely because its front/back horizons coincide. Sky rejection, full-resolution hit depth, mip-0 color and world-space radius checks still apply. This footprint belongs to the tenant's visibility-bitmask algorithm; no new extras or Anomaly helper are required.

For temporal GI, dark history is not an upper bound on current illumination. Prism uses positive current-neighbor luminance for its 12x outlier clamp and permits sparse hits when neighbors are black. Its manager-owned history targets are explicitly released on resize/device end, catalog wrappers invalidated before release, and replacement-device callbacks rebuild shaders and targets. WARP checks cover 50 sector-mask cases and the complete temporal shader; lifecycle state tests use tracking manager stand-ins. Live visual acceptance and hardware timing remain pending.

### Checked pack registration (2026-10-01, implemented)

HDR's registration audit found that the legacy void `Register` hides rejected
manifests and scan failures from packs. The shared fix is
`ShaderPackRegistry.TryRegister(id, root, requiredFullscreenProgramId, out error)`.
It validates the required scanned program before queuing the pack, returns a
diagnostic on rejection, and preserves legacy `Register` callers. Acceptance may
precede Anomaly initialization; it does not promise shader compilation or final
pass ownership. HDR checks for `hdr.tonemap` before installing render patches.
See `Docs/ShaderPacks.md` for the reflection contract. GPU/readiness checks remain
the normal fullscreen/status contract, rather than a pack-private registry probe.

Goal: AfterAtmosphere volumes (clouds, future fog / godrays) light and cheapen without each pack inventing Keen `frame_` field layout, IsolatedMix multipliers, or step LOD. Compatibility-safe: append extras CB fields; helper in `AnomalyFullscreen.hlsli`. Do not steal atmosphere t5. Do not Harmony-patch `MyAtmosphereRenderer` for lighting. IsolatedMix stays over (`src + dest*(1-src.a)`); packs write **LBuffer energy**. No `IsolatedMixLit`.

Notation: [wiki/Framework-gaps.md](../wiki/Framework-gaps.md).

### Final Frontier celestial backgrounds (2026-09-18)

Sun-glare ownership updated 2026-10-02: `SetNativeSunGlare(id, enabled)` controls
only the exact solar light; no world mutation. It defaults on for other tenants.
FinalFrontier claims it for one RHUD-controlled glow and removes its former rim
halos. The original same-frame success gate missed Keen's transparent worker,
which can record before lighting `ConsumeWork` draws the sky. The shared registry
now retains a successful main-draw certificate for the current or previous frame
and clears it on main-pass fallback/device release. Failed, disabled, conflicting
and unregistered providers restore native handling; stale certificates expire.
Initial activation and already-recorded work can have a transition frame.
Twenty-four checks execute the actual registry and installed-game Harmony
prefix on both runtimes; they seed the draw certificate/clock rather than run
the game scheduler. FinalFrontier passes 87 GPU and 84 integration checks.
See [ownership contract](CelestialBackgrounds.md) and `Tests/CelestialGlare`.
Partial-disc visibility and full in-game temporal/glow acceptance remain open.


The initial [celestial contract](CelestialBackgrounds.md) is implemented and builds on both runtimes. FinalFrontier phase 1 uses it. Main/probe shader output passed offscreen D3D11 WARP checks. User validated replacement, brightness, zoom, sun sizing and day/night tracking; reflection refresh, lifecycle and upscaler validation remain pending.

- [x] Exclusive `CelestialBackgroundRegistry`: depth-masked BeforeFullscreen/AfterLighting main view and post-Keen probe background, preserving foreground lighting and main sky fog. Both shaders compile before activation; missing hook, conflict, failure or MSAA falls back to vanilla. No LightDir fork.
- [x] Copied 64-float uniforms and optional immutable float4 data (16 MiB cap), using host buffers and actual render contexts. Probe-time bind/unbind is explicit. Dedicated b6 view CB is 80 B; existing extras remain unchanged.
- [x] View-specific direction, pre-discard directionDx/directionDy derivatives for pixel-integrated stars, and derivative footprint; main scene projection unifies stars and sun. Offscreen tests cover no translation parallax and narrow-FOV finite output.
- [ ] Phase 1/3: validate actual infinity motion, jitter, FOV changes, probe refresh and upscalers in-game. Current depth-zero camera velocity is unmodified, not a proven defect. MSAA sample-aware background replacement remains open.
- [x] Phase 4: sun-only glare ownership/restoration, including worker-before-main-draw ordering. FinalFrontier uses one customizable glow; other providers retain the opt-in API default.
- [ ] Phase 4: partial-disc visibility and in-game glare/bloom transition acceptance.
- [ ] Phase 5: optional projected labels through shared Rich HUD. Existing `HudOverlayRegistry` remains corner text only; shader guides must be excluded from probes.

Tenant design: `FinalFrontier/Docs/EnvironmentRenderingPlan.md`; validation: `FinalFrontier/Docs/IntegrationSpike.md`. Data indexing and optical polish remain later pack work. No changes to existing 320 B extras or shipped Slice AI behavior.

- [x] `AnomalySunColor` / `AnomalySunDiffuse` / `AnomalySunToward` / `AnomalySkyLuma` on b6 extras from `EnvironmentLight` (`PassExtrasCb.hlsli`, 288 B). `FrameTemporal.SunColor` / `SunToward` / `SunDiffuse` / `SkyLuma`. Fail closed when Environment is missing.
- [x] IsolatedMix `src.rgb` is **LBuffer energy**. Light with `AnomalySunColor * AnomalySunDiffuse`. `passes[].scale` is pixel cost, not lighting. No `IsolatedMixLit`.
- [x] `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)` in `AnomalyFullscreen.hlsli` — camera-to-volume × `AnomalySafetyScale`. Never per-ray `tMin`.
- [x] `AnomalyVolumeViewLod(camToVolumeMeters, farMeters)` — 0 at the volume / visual air, 1 at `farMeters`. Packs fade shape / far-cap from this. Complementary to `AnomalyMarchSteps` (slam/TDR). Extras stay **320 B**. Do not invent a binary orbit flag.
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

### Aurora temporal distance follow-up (2026-10-01, implemented; live validation pending)

Aurora's review reproduced two `IsolatedAdd + ContributeVelocity` contract gaps on
D3D11 hardware. The FP16 isolated scratch clamps a valid **89,664.89 m** hit to
**65,504 m**; `IsolatedVelocity.hlsl` reconstructs motion at that shorter distance.
Also, a black `(0,0,0,50)` pack pixel replaces geometry motion `(3,-2)` with
camera-only `(0,0)`. Alpha currently serves as both contribution coverage and
unscaled distance, and the host does not check emission before contributing.

- [x] `FullscreenPassRegistry.SetVelocityDistanceScale(programId, metresPerAlphaUnit)`
  negotiates an opt-in scale. Default **1** preserves legacy raw metres. Accepted
  scales are finite values in **[1, 1,000,000]**; invalid calls return false without
  changing state. Set before or after registration; retained across program reload
  and device release. Packs encode `hitMetres / scale`; the contributor decodes
  `isolated.a * scale`. Aurora requests **1000** and falls back to **1** if an older
  host lacks the API or rejects it. FP16 scratch and 320 B extras stay unchanged;
  the isolated-velocity constant buffer reuses padding for the float scale.
- [x] Aurora writes alpha **0** for empty volume emission and for surface-only
  glow, preserving geometry motion without changing RGB. The host retains base
  velocity for invalid/nonpositive decoded distance or invalid history. Positive
  alpha still claims contribution; packs must clear it for uncovered pixels.
  This is an `IsolatedAdd` distance contract, not a separate opacity carrier:
  `IsolatedMix` alpha remains opacity and needs another product if both are needed.

Evidence and runnable exact-shader probes:
`C:/Users/Zeridian/Documents/ChatGPT/Space Engineers Shaders/Reviews/Aurora-2026-10-01/`.
Linked Aurora lifecycle/UI tests and compiled Anomaly contract tests pass **120
assertions per runtime** on net48 and net10. D3D11 probes preserve all RGB bits
across 8,294,400 synthetic pixels; scaled translation is checked from 1 m to
1,000,000 m and legacy scale-1 motion matches the old contributor. In-game
upscaler, resolution and device-reset validation remains pending. These are local
source changes, not an installed deployment.

### Transparent consumer bindings (updated 2026-10-02; live validation pending)

Aurora enabled was reported to suppress thruster billboards. Keen's
`MyTransparentRendering.Render` binds pixel `b0 = MyCommon.FrameConstants`
before atmospheres. `MyBillboardRenderer.BindResourcesCommon` rebinds vertex
b0 but inherits pixel b0 and standard pixel samplers. Isolated velocity
contribution replaces/clears pixel b0; fullscreen composition changes s0-s2.
The following soft-particle billboard shader uses the inherited projection
matrix for depth/fade, so a missing buffer suppresses emission.

- [x] `OwnedPassRegistry.Run` restores vertex/pixel frame constants, standard
  pixel samplers and the shadow sampler in its AfterAtmosphere `finally`, using
  `TransparentStageBindings` and Keen's cache-aware wrappers on the same rc.
  This includes ordinary tenant failures and the no-fullscreen path. No pack
  patch, pass relocation or removal of temporal contribution is required.
- [x] Linked production restoration passes 32 D3D11 WARP assertions per runtime
  on net48 and net10. The probe reproduces zero emission with missing b0,
  restores exact pixels and sampler addressing, exercises immediate/deferred
  contexts, failure cleanup and repeated cached binding. Engine adapters model
  Keen's skip-on-identical-reference behavior; actual thruster scene confirmation
  remains pending. See `Tests/TransparentBindings`.

- [x] October 2 Star Dust follow-up: FinalFrontier reports particles spawning but
  both native and enhanced modes are invisible. DLSS/FSR output-depth preparation
  clears native context and reference caches after AfterAtmosphere cleanup.
  `BillboardResourceBindingsPatch` now restores the same known resources in a
  postfix on `MyBillboardRenderer.BindResourcesCommon`, after those clears and
  immediately before native pixel consumers. It covers original and replacement
  LDR/PostPP draws without changing render order, materials or foreground depth.
  No pack-specific rendering patch or brightness workaround is needed.
- [x] The expanded runner compiles the installed `Billboards.hlsl` SOFT_PARTICLE
  pixel shader. Synthetic atlas/billboard/vertex inputs reproduce lost pixels
  with missing PS b0 or a changed atlas sampler, recover exact healthy pixels,
  and preserve soft foreground clipping. It also installs the actual built
  consumer hook on the installed renderer. There are **43 assertions per runtime**
  across immediate/deferred WARP contexts. Full game/upscaler scheduling, visual
  acceptance and hardware cost remain pending. Builds used disabled deployment.

## Slice AJ — night / sky illuminant

Keen `MyEnvironmentLightData` has no night-sky RGB. `AnomalySkyLuma` is Rec.709(`SunColorRaw`)×`AmbientDiffuseFactor` — a daylight proxy. Packs that hdr-lift it paint IsolatedMix night white.

- [x] `AnomalySkyAmbient` float3 on b6 extras (**304 B**). `FrameTemporal.SkyAmbient`. Unlifted `SunColorRaw * 0.028` only. Never `AmbientForwardPass` (Keen adds probe `LastAmbient` into that field, up to AmbientMaxClamp ≈ 0.3). Never AmbientDiffuse or pack HdrLift. Fail closed to zero when Environment is missing.
- [x] `AnomalyVolumeAmbient()` in `AnomalyFullscreen.hlsli` — returns extras ambient, capped at `AnomalySunColor * 0.028`. Independent of `AnomalySunVisibility`.

**Slice AJ done when:** IsolatedMix night volumes light from extras ambient without hdr-lifting SkyLuma. **Shipped.** Volumetric Clouds is the first tenant.

## Slice AK — planet air column / visual atmosphere ceiling

Geometric `AtmosphereRadius` (Bruneton mesh, often 1.75× average radius) sits outside Keen’s visual scattering limb. Gameplay air top is `AverageRadius + AtmosphereAltitude` (`LimitAltitude × MaxHillHeight`). AfterAtmosphere IsolatedMix already ignores the proxy’s depth (`DsvRo`). A `0.90 × AtmosphereRadius` deck pokes above the optical edge. Clipping the march at cloud inner / `AtmosphereRadius` is a spherical limb when the camera rises. Volumes may sit on terrain (`MinimumRadius`) if inner/outer are density, not occluders.

- [x] Append-only extras on b6 (**320 B**): `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil`. Radii are **meters from the planet center** (not camera-relative). Fail closed to 0. Game-thread `PlanetAtmosphere` samples the nearest `HasAtmosphere` / CloudLayers `MyPlanet`; `FrameTemporal` copies onto extras. Packs resolve `ClientPlugin.Shaders.PlanetAtmosphere.TryGetRadii` by name, or read the extras in HLSL (`AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade`).
- [x] Do not Harmony `MyAtmosphereRenderer`. Do not skip `Atmosphere_sphere.mwm`. `m_atmospheres` is private — do not reflect it.

**Slice AK done when:** IsolatedMix volumes clamp height to extras air-top / visual ceil without inventing `0.90 × AtmosphereRadius`. **Shipped.** `VisualCeil` equals `AirTop` today. Optical Rayleigh rim vs gameplay air top is Slice AP.

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
- [x] Helper `AnomalyVolumeNight(albedo, sunVis)` (3-arg destRgb overload ignored): night inscatter = AJ day fill × `AnomalyVolumeNightScale()` (0.02 look test; was 0.05). Day fill stays AJ.
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

## Slice AO — pack sparse volume / SVO catalog (shipped)

Planet-scale cloud SVOs (and any future aurora / dust brick field) need pack-owned **node pools** and **3D brick atlases** beyond the three Texture3D medium slots and camera froxels (≤8 km).

- [x] Catalog publish for **StructuredBuffer** node pools (`PublishedBuffer.PublishStructured`) and Texture3D brick atlases (`Publish3D`). Width/Height/Depth/Format on `ISharedBuffer`. `Publish` stays the unique 2D name — do not overload it (pack `GetMethod("Publish")`).
- [x] Fullscreen pack extras **t12 / t13** (`AnomalyPackSrv3` / `AnomalyPackSrv4`) so IsolatedMix can keep `clouds.weather` on t9. t10/t11 stay tiled lights. `FullscreenPassRegistry.IsPackSrvSlot`.
- [x] Optional pack **compute bake**: `PublishedBuffer.PublishBake` / `Uav` — Anomaly does not dispatch; the pack writes the UAV, IsolatedMix reads `Srv`.
- [x] Do **not** store planet SVO density inside Anomaly froxels (those stay shared near lighting).

**No current tenant.** Slots stay for a future stored-density field (placed volumes, a simulated brick cache). Volumetric Clouds does not publish a node pool or brick atlas. Its coarse skip is the live 64² weather × height profile; the 128³ Perlin-Worley stays a procedure. Do **not** bake that noise into bricks. Do **not** steal weather. Do **not** bring back a wrapping 128³ weather cube.

**Slice AO done when:** a pack can Publish an octree node buffer + brick atlas without overloading shape/detail names, and Medium/Fullscreen can bind them by catalog name with known depth. **Shipped.**

## Slice AP — optical visual ceil vs gameplay air top (open)

`VisualCeil` equals `AirTop` (`AverageRadius + AtmosphereAltitude`). IsolatedMix at that radius still sits past Keen's blue Rayleigh rim from orbit — the scattering limb is tighter than `AtmosphereAltitude`. Packs must not invent `0.90 × AtmosphereRadius`.

- [ ] Publish `AnomalyVisualAtmoCeil` as the optical IsolatedMix cap (tighter than `AirTop`) without changing `AirTop` or extras size (already two floats).
- [ ] Do not Harmony `MyAtmosphereRenderer`.

**Workaround (Volumetric Clouds):** Min and Max share one unit: 0 = sea, 1 = `lerp(hills, visual ceil, AtmosphereFit)`. The gate feathers type decks; it is not the march sphere. Max > 1 continues past that lid in the same metres. Do not invent `0.90 × AtmosphereRadius`. Label `// GAP: Slice AP`.

**Slice AP done when:** IsolatedMix at Max 1 lines up with the visible blue limb without packs guessing a fraction of `AtmosphereRadius`.

## Slice AQ — celestial art Texture2DArray (shipped)

Final Frontier (and any future celestial overlay) needs colored fantasy artwork aligned to constellation frames. Stroke sketches in the float4 data buffer cannot match illustrated art.

- [x] `CelestialBackgroundRegistry.SetArt(id, width, height, slices, rgba)` — pack supplies RGBA8; Anomaly owns an immutable Texture2DArray at **t2**, Linear **s0**; null clears.
- [x] `AnomalyCelestialArt` / `AnomalyCelestialArtSampler` in `AnomalyCelestial.hlsli`. Unbound samples fail closed (transparent).
- [x] Bind/unbind only inside the celestial draw; do not steal fullscreen pack t7–t9.

**Tenant:** Final Frontier loads `Assets/Celestial/Art/*.rgba` and calls `SetArt`. Figure index = array slice; local UV uses flipped figure right for reading order.

**Slice AQ done when:** a celestial provider can sample pack-owned fantasy art without Harmony, publicizer, or a second compile hook. **Shipped.**

## Slice AR — AfterLighting screen-space gather sky (shipped)

SSGI (and any future SSR / screen-space gather) must not treat complementary-depth / far-plane LBuffer as a surface. AfterLighting is before Keen atmosphere, but those texels may still hold last-frame atmosphere or Keen environment. SSILVB then applies that color as a huge environment light — cyan GI on foliage facing the sky.

- [x] `AnomalyIsForeground(linearDepth, farMeters)` in `AnomalyFullscreen.hlsli`. Complementary-depth 0 reconstructs to ~far (`LinearDepth.hlsl`).
- [x] `AnomalyForegroundCount(uv, far)` — 0–9 on full-res `linearDepth`. `AnomalyIsSolidForeground` is count == 9 (thickness / first-hit behind a surface).
- [x] Illuminants use a supermajority (count ≥ 6). Isolated sky leaves are 1–3; silhouettes / window edges still bounce. 9/9 rejected too much colored GI.
- [x] Prism SSILVB assigns a minimum one-sector footprint to accepted counts 6/7 with zero depth thickness. Thickness extrusion still requires count ≥ 8. A zero-width horizon must not silently reject an accepted thin illuminant.
- [x] Lighting uses full-res `linearDepth` at the sample UV. Half-res `hiZ` at a sky UV can pull a neighboring foliage depth.
- [x] Gather color is mip 0 only. `litMips` averages canopy holes (sky / last-frame atmosphere). World-space `Radius` caps hit distance. Extras stay **320 B**.

**Tenant:** Prism.SSGI Trace. Do not Harmony atmosphere. Do not sample AfterAtmosphere dest as the gather color.

**Slice AR done when:** AfterLighting gathers reject sky / far-plane hits and do not light last-frame atmosphere or canopy-hole mips, without flattening legitimate bounce. **Shipped.**

## Slice AS — AfterAtmosphere occupancy / skip distance (shipped)

Planet-scale AfterAtmosphere volumes (clouds, later aurora / dust) spend most samples in empty air. Uniform `dt = chord / steps` through a filled shell is the UHawk “GPU computing 0” problem. Occupancy so empty space takes a large step, and occupied cells take the fixed `dt`.

- [x] Extras tail (still **320 B**): `AnomalyVolumeSkipFloor` / `AnomalyVolumeSkipMul` replace `AnomalyPlanetAtmospherePad`. `FrameTemporal` writes `0.38` / `6`. Zero / ≤1 fail closed to those defaults.
- [x] `AnomalyVolumeEmptyFloor` / `AnomalyVolumeEmptyMul` / `AnomalyVolumeIsEmpty` / `AnomalyVolumeSkipDt(occupancy, dt)` / 4-arg override / `AnomalyVolumeSkipDt(occ, dt, maxStep)` / `AnomalyVolumeAdvance` in `AnomalyFullscreen.hlsli`. Soft quadratic empty factor — occupancy ≥ floor stays 1× (no snap). `maxStep` caps empty advance so a thin slab cannot be jumped. Never enlarge `dt` inside an occupied cell.
- [x] Clouds tenant: IsolatedMix advances with `AnomalyVolumeSkipDt` / `AnomalyVolumeAdvance` on the live weather × height profile (lod 0). Do **not** bake a signed-distance Texture3D.
- [ ] Shared occupancy mip if a second volume cannot reuse weather `.r`. Catalog `occupancy` is the 64³ camera clipmap — wrong domain for a planet shell.

**Tenant:** Volumetric Clouds IsolatedMix. Do not Harmony Keen atmosphere. Do not raise Medium steps / Density to fake skip.

**Slice AS done when:** AfterAtmosphere volumes skip empty air through the extras scalars + helpers and do not bake a static SDF. **Shipped;** occupancy source remains pack weather.

## Slice AT — planet-shell weather UV (helper shipped)

Planet AfterAtmosphere volumes (clouds, later aurora / dust) need a **2D weather map on the sphere** plus **per-column** base/thickness. A wrapping catalog Texture3D sampled in camera-relative cartesian 3D cuts cubic slabs. `dot(dir, axis)` weather UV draws small-circle isolines (wavy hill streaks). A weather tile scaled to planet radius makes 10–30 km pancakes on a 1–2 km deck. A global WMO `centerH` is a spherical isosurface.

- [x] `AnomalyPlanetShellMetresFromDir` / `AnomalyPlanetShellMetres` / `AnomalyPlanetShellUv(worldPos, center, hillRadius, tileMetres)` / `AnomalyPlanetSamplePos` in `AnomalyFullscreen.hlsli`. Local east/north metres (`lon * R * cos(lat)`, altitude, `lat * R`). Fail closed (`hillRadius` / `tileMetres` min 1). Extras stay **320 B**. Do not add a second compile hook or a Texture2DArray weather catalog unless a second tenant cannot reuse Texture3D wrap.
- [x] `AnomalyPlanetShellChartWeight` / `AnomalyPlanetShellChartDir`. `atan2` jumps on the −Z meridian, so a **longitude** chart (`lon * R * cos(lat)`) is not one noise tile. Weight is 0 on that meridian and 1 by 0.06 rad. Yaw the direction and reproject **that same point**. Do not rebuild the world position a quarter turn away, and do not floor `cos(lat)` (that disk is the polar pinwheel).
- [x] `AnomalyPlanetShapeArcMetres` / `AnomalyPlanetShapeNorthWeight`. Repeating 3D shape tiles use azimuthal metres from the nearer pole (`theta = acos`, `sin/cos` of longitude). The pole is one point, and `sin/cos` is continuous across the date line. Weather stays equirectangular. Do not sample shape noise with `lon * cos(lat)`.
- [x] Packs must not invent a planet-wide height isosurface from type LUT `centerH`. Height is per-column from weather (or a later catalog).
- [ ] Second shell tenant (aurora / dust) consuming the helper.

**Tenant (Volumetric Clouds):** IsolatedMix uses the helper; Medium duplicates the metres math (`// GAP: Slice AT`) because it does not include `AnomalyFullscreen.hlsli`. Weather tiles are ~16 km × Spacing (Nubis Evolved 2D NDF), map rev 26.

**Slice AT done when:** two planet volumes can sample a shell-parameterized weather field without each packing cartesian 3D → sphere.

## Slice AU — horizon shell continuation (open)

Keen planets are ~60 km across. A few-kilometre cloud deck’s straight chord is only tens of kilometres, so a camera in the weather sees the layer end in empty sky while terrain continues. `HorizonRange` never gets a chance to fade that chord.

- [ ] Helper in `AnomalyFullscreen.hlsli` that keeps a grazing sample on the shell out to a horizon distance. Extras stay **320 B**. Do not scale the planet and do not extrude min/max into a filled slab.
- [x] Clouds workaround: `CloudFlatDeckPos` (`// GAP: Slice AU`). Grazing rays march to `HorizonRange` on the surface. Do not lerp a sky sample down onto the deck. Steep rays and orbit stay on the geometric shell.

**Tenant:** Volumetric Clouds IsolatedMix. Aurora / dust will hit the same chord.

**Slice AU done when:** two AfterAtmosphere volumes can keep a grazing deck out to a shared horizon distance without each packing a private curvature flatten.

## Slice AV — volume ray phase + per-pass history (shipped)

A stable `AnomalyIgn` ray start turns a thin volume into a fixed stipple. Contact shadows and SSGI must keep that hash. Catalog `velocity` on sky is far-plane camera motion. `historyColor` is the composited scene. IsolatedMix alpha is opacity, so `ContributeVelocity` cannot carry hit distance.

- [x] `AnomalyIgnFrame(pixel)` — sixteen-phase `frac(AnomalyIgn + (frame & 15) * golden)`. `AnomalyIgn` / `AnomalyIgnWorld` are unchanged.
- [x] `AnomalyPrevUv(worldCamRel, out prevUv)` — `AnomalyPrevViewProj`, fail closed when `AnomalyLightingHistoryValid == 0` or the UV leaves the viewport. Do not sample catalog `velocity`.
- [x] `passes[].history` catalog name. After that program's draw, Anomaly blits the output into the published RT when the size matches. Missing target or the same resource skips. Copy, do not alias scratch or dest. Extras stay **320 B**.

**Tenant:** Volumetric Clouds studio resolve (view-locked UV) and planet resolve (hit distance + wind delta).

**Slice AV done when:** a volume can animate its own ray phase and snapshot its own history without changing `AnomalyIgn` or the extras layout. **Shipped.**

## Slice AW — Vulkan guest bus (shipped)

Anomaly stays the pack host on either renderer. Core (`Anomaly.dll`) does not reference `VRage.Render11` or SharpDX and does not patch Keen at load. `Anomaly.Dx11.dll` loads only after the factory selects DX11 and keeps today's Harmony, SharpDX, and `MyManagers` path. On Vulkan, a fullscreen program is a copied packet (slot, SPIR-V, bindings, uniform bytes, image ids, compose mode) replayed at six marks. Managed code does not record a command buffer.

Marks, native values: 1 AfterLighting, 2 AfterAtmosphere, 3 AfterTransparent, 4 BeforeTonemap, 5 AfterTonemap, 6 AfterUpscale. Catalog creation is `PublishRgba8`, `PublishVolumeRgba8`, `PublishVolumeMips`, and `PublishStructuredFloats`. A shader outside the fixed bus is stored and not drawn. An empty packet records no draws, so CPU-parity work is unchanged.

**Slice AW done when:** one `anomaly.json` plus one `Fullscreen/AfterLighting` program yields the same pass list on both backends, and a descriptor-free IsolatedAdd guest draw is validation-clean. **Shipped** as the bus. See `Docs/Vulkan.md`.

## Slice AY — bound fullscreen draws (shipped)

A pack shader on the fixed bus (`t0–t13`, `s0–s2`, `b6` extras, `b7` uniforms, owned `b0`) composites onto the world HDR target at its mark. IsolatedAdd, IsolatedMix, IsolatedSub, Chain, Replace, DirectAdd, and PublishOnly are real blends or catalog writes. The extras constant buffer is filled from the renderer frame, and `FrameTemporal` on the core facade returns that frame on Vulkan. Owned programs publish `linearDepth`, `hiZ`, `historyColor`, `reactiveMask`, and `velocity` under those catalog names. A binding outside the bus is skipped. At this initial activation milestone `pointShadowAtlas` was unavailable; the character point-shadow follow-up below supersedes that limitation.

**Slice AY done when:** a descriptor bus shader draws in the guest fixture with validation clean, and a descriptor outside the bus is skipped. **Shipped.** See `Docs/Vulkan.md`.

## Slice AZ — volume, celestial, and color images (shipped)

`VolumetricMediumRegistry` on Vulkan is a copied compute packet. `PublishVolumeMips` keeps the chain, and those volumes bind at `t11–t13`. `CelestialBackgroundRegistry` is a depth-tested packet; `SetNativeSunGlare(id, false)` suppresses stock directional glare once that pipeline exists. Catalog `hdrColor`, `upscaledColor`, and `displayColor` are renderer images. `ClaimUpscale` records the claimant. Bilinear compose stays the default until `NotifyUpscaleComplete`. Removing SharpDX from HdrRender, DLSS, FSR, and frame generation is those packs' change after this handoff.

## Slice AX — GBuffer injection and object motion (shipped)

Stock GBuffer color targets stay three plus depth. Vulkan object motion is a separate RG16F image, not a fourth Keen MRT and not a guest fullscreen program. An extra pack target is another image, not a wider Keen MRT. Do not widen Keen's DX11 GBuffer or add a second compile hook.

- [x] GBuffer SPIR-V injection and an extra `SV_Target` a pack can add.
- [x] Object motion in the catalog `velocity` buffer.

The motion pass replays rigid, generic, cube, and deformed-cube draws with the previous frame's object rows and view-projection. It depth-tests equal to the GBuffer, so a pixel it writes is the surface the GBuffer kept. `CameraVelocity.hlsl` with `ANOMALY_COMPOSITE` keeps that vector and fills every other pixel from depth. Skinned characters, voxels, foliage, and decals stay on the depth fill. A changed draw list drops object history for one frame. The image is guest sentinel `0xFFFF000000000008` at `t1` of the owned velocity pass, and the catalog name remains `velocity`.

A pack adds up to three full targets with `anomaly.json` `"attachments"` or `GBufferTargetRegistry.Request`. Slot 0 is `SV_Target4`. The same name and format share a slot. A mismatched format fails closed. On Vulkan the write shader is `GBuffer/<name>.hlsl` (or `"file"`) with entry `__pixel_shader` and `SV_Target0` out. It declares no descriptors and no push constants. `nointerpolation` object rows are `TEXCOORD0`–`TEXCOORD2`. A shader that only declares `SV_Position` still links. The renderer depth-tests it equal to the GBuffer, using the object-motion vertex shader so the pixel is the surface the GBuffer kept. Catalog `Active(name)` is sentinel `0xFFFF000000000009` plus the slot. `RequestSrv` samples it. Packed `GBuffer1.a` stays a DX11 write into Keen's channel. Skinned characters, voxels, foliage, and decals stay clear on the extra target.

**Slice AX done when:** a pack can add a GBuffer target, and object motion is readable from catalog `velocity`. **Shipped.**

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
| Pack Harmony on Keen passes while Vulkan is the renderer | Submit a guest packet. `Anomaly.Dx11` is the only assembly that patches Keen, and only after DX11 is selected |
| Pack `MyPixelShaders.Create` / pack-owned fullscreen RTs | Anomaly owns `FullscreenPassRegistry` and the scratch pair |
| Last-writer-wins Replace on a slot | Same as silent Overlay overwrite — fail closed |
| Pack-chosen CB slot for uniforms | Anomaly allocates b7; `SetUniforms` is 64 floats / 256 B |
| Steal atmosphere t5 for AfterAtmosphere programs | `DensityLut` is Keen’s and already unbound |
| Pack-private IsolatedMix HDR scale / Keen `frame_.Light` lighting | Slice AI extras illuminant. `Frame.hlsli` layout is not a public contract |
| Per-pack march LOD that floors `AnomalySafetyScale` or uses per-ray `tMin` | Slice AI helper. Grazing chords and spectator slams TDR otherwise |
| Bake a signed-distance Texture3D from live weather on the render thread | Slice AS `AnomalyVolumeSkipDt`. A 200 ms static SDF cannot track wrapping weather and TDRs the test card |
| AfterLighting IsolatedSub reconstruct from interpolator UV / first-N tile lights | LightPoint uses `screen_to_uv(SV_Position)`. Tile lists are unsorted; `maxTileLights` is the global stride. |
| AfterLighting IsolatedSub project with `AnomalyUnjitteredViewProj` / scale contact stepLen by `AnomalySafetyScale` / jittered reconstruct for the march | Unjittered view + `AnomalyViewToDepthUv`. SafetyScale changing step length jumps umbra when the camera looks. |
| AfterLighting IsolatedSub clip at photometric `light.range` / 20% lumaFloor / pixel IGN / longer-range BRDF tail | March past the Keen sphere (~1.4×). IsolatedSub photometric `sum L_i*(1-vis_i)` of the brightest N. Dither with `AnomalyIgnWorld`. |
| AfterLighting IsolatedSub dest-write via destHistory Dest[p] merge | IsolatedSub blends occupancy onto dest (same dest RTV as Replace). Pack writes `AnomalyIsolatedSub`. destHistory merge never reached Present. |
| AfterLighting dest-alias t0 blit leaving mergeCopy bound | Blit before the pack PixelShader. IsolatedSub with mergeCopy bound copies dest onto dest. |
| Light AfterLighting sky / far-plane LBuffer as GI / environment | Complementary-depth 0 reconstructs to ~far. That texel may still hold last-frame atmosphere. `AnomalyIsForeground` then `AnomalyForegroundCount` ≥ 6 (not 9/9). Gather color is mip 0 only. Do not use half-res `hiZ` as the hit depth at a sky UV. |
| Sample Keen shadow cascades from AfterAtmosphere for a planet disk | Cascades are camera-local. `AnomalySunVisibility` uses local-up × sun with atmosphere light wrap |
| Use `AnomalySkyLuma * HdrLift` as night / ambient illuminant | Use `AnomalyVolumeAmbient()` / `AnomalySkyAmbient`. SkyLuma is sun luma × AmbientDiffuse |
| Scale `AnomalySkyAmbient` by `AmbientForwardPass` | Keen adds probe ambient into that field. Night IsolatedMix becomes sun-scale. Use `SunColor * 0.028` for **in-cloud day fill** only. |
| Treat `AnomalyVolumeAmbient` / 2.8% sun as planet-night illuminant | IsolatedMix over night dest≈0 is headlights. Slice AM: `AnomalyVolumeNight` = AJ × 0.02 + monotonic squared-limb transmittance. |
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
geometricNormalView, towardLightView, rayLengthMetres, rayThicknessMetres,
pixelBudget)`. Include AnomalyFullscreen first. It is for current-frame camera
depth only: it uses that frame's projection/reconstruction. Projected pixel
intervals pick candidate texels; a hit is Euclidean metres to the 3D light-ray
segment in that pixel (`thickness` plus a texel footprint), not a view-Z slab.
Exhaustion truncates the ray instead of widening the stride. Fade is the last
10 cm of `rayLength`. Behind-camera endpoints are skipped (do not clip onto
the camera plane). Receiver tangent-plane rejection and a small bias replace
camera-depth separation gates. Maximum budget is 192 intervals.
`AnomalyContactVisibilityExcludingPlayer` has an extra `softTaps` argument
(0/1 = hard first-hit; 2–8 = 1D edge filter on neighbor receivers, perpendicular
to the projected light, ~3 cm virtual source, pixel-clamped). Center is the
receiver, not the blocker UV. Invalid / other-surface taps stay occluded so the
umbra interior is not punched. Default wrappers pass 0 so existing packs stay
crisp. This is not `AnomalyPointShadowVisibilitySoft` (cube-face 4×4 tent).
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
sampling directions use `AnomalyViewToWorldAt0` (extras InvViewAt0 rows), not
`mul(view, frame_.Environment.inv_view_matrix)`. Contact `towardLight` is
LightPoint view-space. Hits are Euclidean metres to that 3D ray segment, not
a view-Z slab. Facing uses GBuffer view N. Behind-camera endpoints skip.
AnomalyPointShadowVisibility is a 1-tap compare. Soft 4×4 tent
(`[1,2,1]/4`, face-clamped) is `AnomalyPointShadowVisibilitySoft` — packs
opt in on High/Ultra only. Shadows reconstructs receivers at SV_Position
pixel centers, matching blocker sampling. GPU regressions pass; live
movement and perspective-switch acceptance remains pending.


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
The fade now uses the last 10 cm of Euclidean distance along the light ray.

A second, short-range scene still missed hits at a 3 m camera offset after that
fix. Unit-pixel intervals anchored at the ray origin crossed cell boundaries
but sampled just one cell. Traversal now splits at exact X/Y pixel crossings,
so Euclidean distance to the light-ray segment is tested at the sampled cell. Subpixel rays receive one
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

### Slice AI follow-up — opaque ray scene and lighting ownership (2026-10-03)

Shadow-source extension (2026-10-04): ABI v1 appends field 14, physical native-light actor origins aligned with the existing native records. Field 6 retains Keen's offset source positions. SE-RT now uses physical positions by default and offers an explicit offset opt-in. This reusable source-position channel belongs to Anomaly; packs do not patch light blocks or infer positions from source IDs. Existing nearby off-screen capture remains; explicit hidden actors stay excluded. SE-RT's shadow any-hit traversal certifies known opaque blockers even through incomplete coverage, while unsupported visibility retains native lighting. Sun retains native distant coverage; finite shadow segments target actual source endpoints. GPU shadow checks and live acceptance are documented by the tenant.

`RayScene` API v1 now captures CPU mesh data before upload release and supplies camera-relative geometry, material/skin snapshots, native lights and spotlight texture profiles, emitter proxies and borrowed DX11 frame resources to render tenants. GPU effects provide approximate sources from CPU emitter metadata; individual particle buffers remain a gap. It invokes tenants at AfterLighting/BeforeFullscreen and exposes completion/reset callbacks for query polling and resource disposal. Disabled tenants can pause frame enumeration while retaining upload capture. CPU captures survive device reset. Missing meshes are coverage boxes, never certified empty space. `OpaqueLightingOwnership` provides signal claims, a pre-composite commit and conflict suppression in owned/fullscreen passes. SE-RT is the first tenant; it contains no Keen pass patches. See `Docs/RayTracing.md` for the array ABI and remaining coverage limitations. Dual-runtime builds and production WARP transport/traversal checks pass; in-game source coverage, visual acceptance and midrange timing remain open.

### Shadow quality extension (2026-10-04; implementation with live acceptance pending)

RayScene v1 appends the optional far-only sun product at field 15 and optional
versioned thin-glass optics at material variant field 4. Field 14 physical
origins and every existing field retain their meanings. Tenants explicitly
request far sun through SetFarSunEnabled; disabled tenants incur no far pass.
Anomaly runs one isolated 2048-square directional depth batch, excludes the
256 m resident sphere by fragment clipping and preserves native depth-material
cutouts/dither. Both mesh render paths route depth shader bindings explicitly;
unverified/unready variants invalidate the product and tenants retain native
sun fallback. Owned clipping resources and maps release on world/device lifecycle.

Known GLASS optical data comes from the transparent registry, excluding its
error material and unresolved textures. The descriptor carries unpremultiplied
linear color/opacity, texture/UV, sidedness, category and revision. SE-RT implements
bounded eight-surface RGB transmission, custom 32/4/2-frame histories, finite
emitters and capped sparse full-resolution transport. Shared contracts remain
Anomaly-owned; SE-RT adds no Keen lighting patches. Generic instance buffers,
individual GPU particle geometry and full refractive/media transport remain
coverage/design limits. See Docs/RayTracing.md and SE-RT/Docs/ShadowQuality.md.
WARP and numerical gates do not establish live shader-hook completeness, image
quality or combined GPU performance; those scene/hardware acceptance gates remain.

## Vulkan lit-mip parity follow-up — 2026-10-16

The shared `RequestLitMips` contract now works on Vulkan and survives pending renderer selection. Enabled pipeline-ready catalog binds also request the default five-level pyramid. Anomaly submits an owned AfterLighting producer; SE-Vulkan copies current HDR into FP16 and generates the dimension-clamped mip chain before consumers. Explicit requests preserve DX11's maximum-until-cleared behavior. No demand clears the catalog alias. ABI-1 packet flag 8 is feature-negotiated; older native libraries report unavailable instead of advertising a base-only image. `Tests/GuestRuntime` verifies demand/order/resize/capability transitions and real sampled HDR mip averages and refresh. Live image comparison and Vulkan validation-layer acceptance remain pending. The occupancy follow-up is below; the point-shadow atlas/playerDepth follow-up is below; the shared froxel/volumeReactive owner is implemented below, with broader acceptance still open.

## Vulkan occupancy follow-up — 2026-10-16

The shared occupancy request now retains pre-selection demand and submits the same depth-splat/AABB-stamp shaders on Vulkan. Anomaly owns stable light selection, local-character exclusion and bounded box work from copied renderer inputs; native only executes packets and owns device resources. The atlas remains 64³/2 m packed into 512², using float storage on Vulkan. Current-frame depth and feature-negotiated compute clear/copied constants are mandatory. Native guest UBO alignment is independent of world initialization, and structured updates retain fence-protected frame buffers. Whole-atlas GPU readbacks and managed provenance/selection/capability checks pass. Loaded-scene geometry, recreation, visual and performance acceptance remain pending; the point-shadow atlas/playerDepth follow-up is below; the shared froxel/volumeReactive owner is implemented in the follow-up below. See `wiki/Framework-gaps.md` and `Tests/GuestRuntime` for exact scope.

### Vulkan character point-shadow follow-up (2026-10-16)

The shared point-shadow/world-box requests now retain pending demand and produce `pointShadowAtlas` and `playerDepth` on Vulkan. Anomaly owns light selection, allocation, six stable light views, optional bounded boxes and catalog headers. SE-Vulkan copies indexed opaque character triangles using the stock homogeneous four-bone skin-position equation; atlas geometry uses a stable drawable LOD and light-relative double subtraction, while player depth uses the current render LOD and camera origin. Native geometry/viewport/far-clear features execute the owned packets with MIN distance blending. Strict GPU checks cover every face, animated pose, optional boxes, observer independence, player depth, headers, resize, spectator cleanup and the actual sampling helper. The shared froxel/volumeReactive producer remains open; live comparison, loaded-world geometry, recreation and performance acceptance remain pending. See `wiki/Framework-gaps.md` and `Tests/GuestRuntime`.

### Vulkan shared-volume resource follow-up (2026-10-16; producer integration open)

The fixed fullscreen packet could not express the existing shared froxel kernels. A separate feature-512 transport now carries their copied b0–b7 constants, t0–t47 inputs, five compute outputs, XYZ workgroups and two RGBA16F reconstruction targets. The original fullscreen packet remains 416 bytes. D32 comparison inputs and R16F volumes are available; shared shaders declare FP16 storage formats explicitly for SPIR-V, and native checks formats/dimensions and rejects incomplete or aliased resources. Strict GPU replay executes the production shared light/injection/integration/reconstruction/coverage kernels against analytic optical-depth, emission, transmittance and reject-mask oracles. Anomaly retains shader/producer policy and native retains GPU lifetime/execution. Active provider/near-interval transactions, current scene shadows, sealed rooms, history/recovery, HDR composite and motion ownership remain to integrate. See the open volumeReactive entry in wiki/Framework-gaps.md; no live/full-volume parity is claimed.

### Shared volume CPU and interior source follow-up (2026-10-16; producer integration open)

VolumeRegistryState now supplies one CPU registration/validation/transaction implementation to both runtimes. The core facade retains pending registration, copied parameters, double origins/bounds, requests, configuration and interval callbacks; DX11 receives the final snapshot after its assets initialize and subsequent calls route to its state. Frame start revokes interval ownership and removes volume products before native queue reset. Callback/reset failures cannot leave a ready volume frame. The fallback present follows disabled providers even when compiled late, and source replacement follows unregister/re-register. Status exposes the shared-volume state separately.

The shared shader composer and game-thread airtight snapshot now live in Backend source, and the Vulkan host updates/releases its snapshot. VolumeInteriorData centralizes sorted cells, topology revisions and seven-row camera-relative grid records; DX11 retains device upload ownership. Current directional shadows, actual shared-frame preparation/history/commit/composite/motion, native publication validity and live/recreation/performance acceptance remain open. The guest medium fallback remains unsuppressed until that complete transaction succeeds.

### Guest live resource phase and shared shadow views (2026-10-16)

The host's callback runs between native BeginFrame and EndFrame. Guest factories previously rejected that open-frame phase even though offscreen replay passed. Native now opens a guest allocation window after BeginFrame and closes it before EndFrame records world/guest commands; replacement waits for previous submissions and the renderer API mutex preserves queue/pool ownership. Feature 1024 gates the normal Anomaly callback, and older native libraries report an update requirement. The strict fixture opens a real menu frame, creates both pipeline contracts, uploads/replaces an image and queues a packet, then checks replay pixels. It does not establish queued-world insertion or live pack parity.

VolumeShadowViews now centralizes the DX11 directional policy in shared source: 2048 resolution, 128/1000/8000 m receiver radii, texel snapping, 32 km sunward reach and double-relative matrices. The DX11 producer uses this helper. Receiver bounds, pole orientation, sub-texel stability, large coordinates and offscreen caster inclusion are checked. Dedicated Vulkan scene-depth queries and the full shared-volume frame transaction remain open.

### Vulkan shared volume frame owner (2026-10-16; broader acceptance open)

GuestDevice synchronizes every device-owned cache before guest submission. A native ResourceEpoch change invalidates ordinary pipelines/storage/history, frame constants, occupancy/point-shadow uploads and volume history together. BufferCatalog invokes retained device-end callbacks and restores cloned CPU-backed textures, volume mip chains and structured floats. GuestColor likewise restores celestial data/array uploads and the upscale claim. Callback failure keeps submission blocked for retry. GuestRendererImage.IsAvailable checks the captured device epoch, including wrappers retained outside the catalog. Disposed contexts report epoch zero. A replacement context initially uses the normal fallback while its stock HDR initializes; complete frames then resume the shared transaction with fresh history. Loaded-world loss/recovery, resize and performance remain separate acceptance work.

GuestVolumes now builds shared kernels and quality-dependent resources, captures current scene/room/planet inputs, and submits one transaction with ping-pong source/coefficient/depth histories. Feature 4096 orders extended flags 1024/2048 before/after ordinary passes within their mark. Preparation precedes AfterAtmosphere packs; HDR transmittance composite and max reactive merge follow them; masked motion overlays camera velocity at BeforeTonemap. Callback failure cancels shared work before ordinary submission and revokes intervals. Fallback packets are suppressed only for a committed captured frame. Catalog handles capture frame/device identity; native pinned products require a current recorded write, and EndFrame consumes frame packets. DX11 also passes actual room completeness into interval commit.

GuestVolumes now runs the shared light/inject/integrate/reconstruct/reactive chain in the actual Anomaly core and stock renderer. Feature 4096 provides before/after phases, distinct attachment aliases and current-write catalog validity. The strict GPU lane passes 268 checks: analytic scattering/reject pixels, linear HDR scene-times-transmittance-plus-scattering with alpha preservation, host reactive merge, history acceptance, callback cancellation/recovery and stale-product rejection. It also covers the independent scene-depth sources. AfterUpscale replay now precedes the swapchain render scope; catalog IDs no longer collide with extra GBuffer attachments. The fixture uses a controlled medium and injected complete empty-room snapshot. Actual context teardown/replacement now also passes: cloned pack/celestial uploads, expired wrappers, retained demand, fresh volume history and empty occupancy/spectator shadow clears. Live multi-pack/world/topology comparisons, loaded-world device-loss recovery, broader motion/material/resize coverage, performance and validation-layer acceptance remain pending; no deployment or full parity claim is made.

### Shared volume frame preparation and atomic batch (2026-10-16; activation open)

The renderer guest bridge now exposes SubmitExtendedBatch with at most 256 copied passes. Each object[] is `[mark, compose, width, height, depth, pipeline, outputs, constants, sampled, flags]`; integers use Int32, pipeline/IDs UInt64, outputs/sampled UInt64[], and constants byte[][]. One native call validates and copies the complete batch before queue insertion. Invalid native resources or managed serialization failure leave the valid prefix unqueued and preserve prior accepted work. Queue capacity is reserved before the nonthrowing commit moves. Existing single-pass calls use this serializer too. GPU checks run the production light/inject/integrate/reconstruct/reactive chain as one batch and verify invalid-tail atomicity. This is queue acceptance, not GPU completion or provider-interval activation.

Backend/VolumeFrameData now owns the 384-byte frame, 224-byte shadows and 1280-byte provider layout used by DX11 and the guest preparation path. Provider origins/bounds subtract the copied camera in double precision. The guest builder derives projection coefficients from its camera-at-zero matrices, clamps dt, requires finite environment inputs, and rejects history on an unsafe camera frame. The GPU fixture consumes the shared frame struct; CPU checks cover rotated reversed projection, history safety, field sizes and large-coordinate provider bytes.

PlanetAtmosphere now compiles from Backend source in core and companion. The core samples/clears its own device-free game-thread snapshot on both renderer routes, retaining the existing pack-facing type and radii API. Vulkan queries no longer depend on loading the DX11 companion. The optional --core regression loads the actual built Anomaly assembly and verifies snapshot/radii/clear behavior and absence of DX11 assembly references with an injected CPU snapshot; it does not simulate a loaded planet. The later frame owner now connects scheduling, history, interval commit, composite/motion and current publication validity. Loaded-world device-loss, live pack/topology and performance acceptance remain open.

### Independent Vulkan scene-depth replay (2026-10-16; volume activation open)

SE-Vulkan feature 2048 accepts up to three Anomaly-owned D32 targets and camera-relative projections before BeginFrame. Explicit world-packet bits 8–10 select these views; separate projection slots 73–75 preserve all eight stock cascade and 64 spot slots. Regular depth-material variants render them before GBuffer/guest consumers. Targets clear per request, requests are consumed once, and recorded-view validity resets each frame. Stock shadow cache signatures exclude guest-only membership.

BackendRuntime.AddSceneListener supplies copied double camera and sun direction before frame collection. Anomaly GuestVolumeShadows owns the shared directional policy and three D32 allocations; GuestBridge.ConfigureSceneDepthViews copies world projections and derives camera-relative native matrices. The separate collector visits actor and active voxel registries without main-view/distance or resident/GPU ownership skips. It reuses real meshes, instance streams, skin palettes, alpha materials and voxel constants; missing caster models, unresolved alpha or preparation failure cancel the request. CaptureSceneDepth reports current generation, completeness, actor/draw/cell counts and status. Main-view LOD state is restored. Ordinary stock visibility and SkipInDepth remain authoritative.

The strict GPU fixture covers analytic offscreen rigid depth plus installed main-hidden armor through the production scene collector, ownership flags, hidden/empty-frame clears and source failure cancellation. Separate managed checks cover demand, capability, copied world matrices/camera and hook rejection. The expanded 59-check GPU fixture also covers cube/generic streams, a nonzero stream offset, static groups, an installed astronaut with an updated bone palette, and an active main-hidden voxel cell. This does not prove native residency lifecycle or complete live scene coverage. Validation layers, loaded-world comparisons, recreation/performance and the full shared volume frame transaction remain pending; provider near intervals stay unclaimed.

HDR snapshot barriers preserve the layout advertised to consumers; see [VkDescriptorImageInfo](https://docs.vulkan.org/refpages/latest/refpages/source/VkDescriptorImageInfo.html). Readback of the borrowed HDR sentinel is allowed only after a completed world frame and returns RGBA16F; it never transfers ownership.

The native snapshot follow-up separates HDR from tone/upscale scratch images, retains replaced images until the corresponding frame fence (or synchronous replay) completes, and copies HDR using its render dimensions even at AfterUpscale. The 268-check actual-core GPU lane verifies simultaneous distinct HDR/tone reads and render fractions 1, .5, .75, 1 through the stock tone path. This is snapshot/resize coverage; it does not certify the full volume owner's dynamic-resolution history, vendor-upscaler output, loaded-world device loss, real multi-pack/topology or performance acceptance. Native Release tests pass 15 with one optional reference skip; validation layers remain unavailable.
The shared-volume stage follow-up now matches DX11: named volume products bind only after AfterAtmosphere fullscreen work, and the motion overlay runs before BeforeTonemap fullscreen consumers. GPU checks force distinct camera/volume vectors and verify the ordinary consumer sees the overlay. The real FinalFrontier Fog and VolumetricClouds provider sources also run through the actual core transaction with controlled mipmapped noise and the production 64x64x2 weather layout. Fog matches its configured exponential extinction; combined provider extinction matches the sum of separate contributions at every froxel. Missing weather revokes both near intervals and the current reject product, and restoration recovers. CPU-backed image, structured and mip uploads now reuse their native IDs; updating weather bytes removes cloud extinction while preserving fog. These are controlled production-shader tests with injected complete empty-room provenance, not loaded-world or full plugin-adapter acceptance.
Volume-owner resize and validation follow-up: SevkGuestFrame now reports the planned render dimensions in the open-frame callback, using the same logical viewport/fraction policy as EndFrame, and reports current presentation dimensions separately. The earlier presentation-size metadata caused producer allocations to remain oversized under DRS. Fresh sampled guest images now clear deterministically and transition every mip/layer to the advertised shader-read layout before first use; initialization does not mark a catalog product produced. Postprocess images include transfer-source usage for legal HDR/tone snapshot copies. The strict GPU fixture now discovers the repository-private Khronos layer when the caller has not configured one, and checks validation errors after the entire recreated-device lane. Earlier validation_enabled=0 results did not exercise the available private layer.
Current four-feature validation evidence: the strict actual-core lane passes 268 checks with validation_enabled=1 and no Khronos validation errors. It covers 50%, 75% and full render fractions, 95x73 output and 48x37 partial workgroups, automatic old-history rejection followed by new-size recovery, expired handles, and resized playerDepth/litMips/froxel products. It also retains the earlier real Fog/Clouds, callback, recreation, geometry and snapshot checks. Native Release passes all 16 tests with the private layer enabled, including the formerly optional opaque reference. The earlier no-layer results above are historical: the layer is available at SE-Vulkan/Native/build/validation/Bin and the strict GPU runner now discovers it automatically. Loaded-world comparison, full plugin adapters/vendor upscalers, actual device-loss faults and performance remain acceptance work; this does not claim full live DX11 parity.

Slice AI live topology follow-up: physical grids need authoritative engine gas topology even when no vent supplies oxygen. The common snapshot retains ForcePressurize while tracking, starts initial generation if the engine has not scheduled it, and restores its prior flag while removing only its own callbacks. Previews/render-only grids are excluded; pending physics, missing gas services, rebuilding rooms and budget overflow keep the frame incomplete. Ten actual-core lifecycle checks pass. The first private Creative run proved occupancy/lit mips and real copied-character point shadows, but failed the volume gate: its saved EmptyWorld disabled oxygen and pressurization. The corrected private-copy harness enables both before load without changing the source save or player configuration. An unavailable gas service remains a fallback condition; lifecycle tests and captured metadata do not establish live room/visual parity.

The loader overrides Sandbox.sbc settings with Sandbox_config.sbc, so parity preparation now updates both files in its private copy. With those preconditions, live Vulkan run 31f275d05c3d4781aacbc2ebbc0852a6 reached 295 shared-volume commits and concurrent availability of every requested producer. Its fixed overview camera then exposed a bridge bug: projection scales came from the combined view-projection diagonal, which can become negative under rotation. The bridge now reads Frame.Environment.projection_matrix instead. A rotated-camera actual-core GPU regression and corrected live off/on captures are required before closing this acceptance item; the failed run and its off capture remain diagnostic evidence.

Projection correction validation: 273 actual-core GPU checks pass with validation enabled and zero reported Vulkan errors. The additions exercise the failing rotated overview direction, verify projection scales against the pure projection matrix, and return to the initial camera. Native Release tests also pass, with the optional opaque-reference test skipped in this invocation. Corrected live on/off capture comparison remains required.

Live Vulkan run 241403d2adea45149545b157f21c225c now passes concurrent producer readiness and fixed-camera off/on capture delivery, including re-enable/1000 m near-interval recovery with engine topology. Reviewed captures show the controlled medium's extinction/emission and clear restoration when disabled. Validation remained enabled with zero reported errors and zero device loss; the managed D3D11 observer recorded no creation attempts. The broader simulation harness later failed its powered-door open/close check, so this is a producer/capture pass rather than a full fixture pass. Matched DX11 comparison and real sealed-room transitions remain required.

The matched DX11 diagnostic revealed a shared API dependency gap: without a manifest pack, public occupancy/shared-medium requests did not keep OwnedBuffersPass linear depth active. WantDepthBuffers now includes explicit occupancy demand and requested enabled media, retaining the existing manifest/debug demand. Core/companion builds pass on both runtimes; live verification remains required. Parity fixtures now set an explicit finite sun direction on both routes and include a ventless one-cell shell that opens and reseals, comparing actual gas topology against the active backend's shared snapshot before allowing the producer gate to pass. The earlier DX11 diagnostic run (9ebdc4973ed74608b1d7f27ba915ce32) remains failure evidence, not a matched comparison.

The finite-sun DX11 run verified producer availability and sealed zero-oxygen exclusion, then exposed the engine's ForcePressurize-only wall-removal gap: its incremental task receives no deleted block and leaves the room sealed. The common tracker now invalidates on grid add/remove/integrity and door state events and requests full generation after any in-flight task finishes. Multiple edits coalesce; stale generations cannot publish. Disposal removes these subscriptions and restores prior force demand. Thirteen actual-core lifecycle checks and dual-runtime builds pass. Live open/reseal verification remains pending.

Corrected DX11 run a83b4531c5ac4d5883bb4415b68698f7 passes the real sealed-zero-oxygen/open-wall/resealed snapshot check, all five catalog products, shared interval recovery and reviewed fixed-camera off/on captures. It also completed the broader fixture simulation and controlled unload/exit. The matched Vulkan room-refresh run remains required before claiming both routes passed that transition.

Matched Vulkan run 7c0c93ed3506463ebac77222c6c31f56 also passes the room transition, five-product gate, interval recovery and reviewed off/on capture delivery, with SDK validation enabled/errors=0 and no observed managed D3D11 creation attempts. The controlled enabled sky region differs from DX11 by about 3/3/2 display RGB levels; this is not a whole-image equality claim because base lighting differs. The wider Vulkan fixture again failed its door cycle; the harness now waits for actual fully-open state before closing to avoid collapsing timed commands after cold compilation. That correction is pending live verification. Full adapter/vendor-upscale/device-loss/performance acceptance remains open.

The door refinement passed its assertion in run 763298d91a0747d29e713c363c934832, which also passed both Anomaly gates; broader completion then failed missing LCD captures. The request log shows multiple stages collapsed into one post-compilation update. Parity-only scenario time now caps each update's advance at 100 ms while wall-clock timeout remains bounded. This fixture scheduling correction is pending its canonical live rerun; no additional producer algorithm changed.

Final four-producer acceptance (supersedes the earlier pending run notes): Vulkan run 7d7c38e694614d43a63267a3686fa8ff passes topology, current producer readiness, interval recovery, all capture delivery and the broader simulation checks, then unloads/exits cleanly. Door maximum is 1 and final ratio 0; the astronaut travels 11.77 m. Validation stays enabled/errors=0, device-lost=0 and the managed observer records no D3D11 creation attempts. Reviewed off/on captures retain the expected medium/clear behavior. Together with DX11 run a83b4531c5ac4d5883bb4415b68698f7, 273 validation-clean actual-core GPU checks, 13 topology lifecycle checks and dual-runtime builds, this closes the four named missing-producer gaps. Whole-renderer image equality, wider adapters/vendor upscalers, actual device-loss faults and performance acceptance remain separate work. See the current acceptance block in wiki/Framework-gaps.md.

### Slice AI follow-up — shader parity registration/resource foundation (2026-10-21)

Shared gaps belong to Anomaly's host and SE-Vulkan's native executor. Pack-local
workarounds are unnecessary for format-aware catalog targets, floating-point
clears, required-program admission, source parsing and resource retirement. The
shared immutable manifest model replaces both regex parsers, retains temporal
metadata and uses the existing register reservations. The shared stage catalog
is now compiled into the device-free core as well as the DX11 adapter.

Native resource commands use a checked 48-byte versioned packet and feature
bit13. Timeline retirement replaces resize-wide device waits; identical image
ensures preserve content. Feature bit14 separates native pass recording and
submission completion from successful compilation. Context epochs prevent a
retired old-context pipeline ID from deleting a new-context pipeline with the
same number. Failed partial pipeline factories release their created variants.

Focused CPU and real GPU fixtures cover malformed/nested manifests, admission
rollback, target ownership/resize, FP16 HDR and alpha -1 clears, idempotent create
and release, older-host refusal, and creation-versus-execution status. Validation
was explicitly enabled with synchronization checks and reported zero errors.
This foundation does not close the remaining fullscreen, named-stage, attachment,
HDR/vendor, lifecycle stress or Windows multi-pack acceptance gates.

### Shared frame/environment parity — 2026-10-21 candidate

DX11 and Vulkan now use the same device-free lighting and motion-safety
calculation. Anomaly receives raw sun color, diffuse/ambient factors, normalized
toward-sun direction, planet air/visual radii and camera deltas formed from
double-precision absolute positions. Stock Vulkan light inputs are unchanged.
The guest boundary also normalizes/negates stock direction when no adapter input
is present. Native feature bit15 carries a checked96-byte environment packet
keyed to the current open renderer frame. Fullscreen b6 preserves safety zero
and publishes planet/skip constants.

FrameTemporal getters read the callback's immutable frame-index snapshot; they
no longer perform wall-clock-based native queries. Camera cuts invalidate the
current history immediately and reset the shared motion envelope. The shader
probe verifies actual b6 output for raw color, direction, zero safety and planet
radius. CPU tests cover rotation,80m cuts,1/16 recovery,1e12m origins and repeated
getters. Native17-test suite passes; actual-core depth/volume/recreation suite
passes302 checks with validation enabled and no reported errors. Full matched
Windows adapter/visual/performance acceptance is still required.

### Fullscreen isolation, histories and output — 2026-10-21 candidate

Vulkan fullscreen passes now render an isolated product first, publish that
product and capture its history, then compose using Anomaly's DX11 merge
shader equations. Add/sub preserve destination alpha; mix saturates source
opacity. No stock blend mode is changed. Chain state is local to its insertion
slot and a final Chain is copied into the actual destination. The frame holds
registration/settings while copying commands, and publishes all previous-frame
aliases before building any consumer. Disabled/re-enabled and resized histories
start invalid; production checks native recording/frame age before reuse.

Half/quarter dimensions use DX11 floor division independently of renderer DRS
rounding. AfterUpscale uses output dimensions. Native feature bit16 supplies
a renderable, bilinear output for requested AfterUpscale work when no upscaler
is active, plus exact output readback. Owned HDR history alone does not request
that extra output path. Renderable display images include color-attachment
usage. Offscreen consumers snapshot the current tone/upscale image correctly.

Evidence:2,800 independent GPU composition pixel comparisons cover every merge
equation, negative/over-one alpha, HDR, destination alpha and odd-size scaling.
Maintained packet tests cover all compose histories, early consumers, Chain
completion and slot separation. The actual-core324-check lane verifies final
AfterUpscale Chain pixels and dimensions at50/75/100% without an upscaler in both synchronous and pipelined recording, with
validation enabled and zero reported errors. Full combined Windows live visual
and performance acceptance remains pending.
### Portable callback preparation and temporal parity (stage 5)

The shared OwnedPassRegistry now provides RegisterPortable and a renderer-free
command context; legacy DX11 callbacks retain their contract. Vulkan ordinary
passes participate in reactive/velocity production, including distance-scaled
alpha. Frame membership/settings/history freeze while renderer-thread uniform
preparation remains available. Native bit17 covers portable target execution,
including R16_FLOAT; unsupported hosts fail with an update instruction.

Aurora, clouds and contact shadows use portable uniform preparation. Prism's
guest preparation now runs before the AfterLighting fullscreen chain, maps
0–7 iterations and the configured history cap, and stores world normals so
normal rejection remains valid across camera turns. Production Temporal/Moments
readback tests cover ages, rejection, rotation, first frame and history caps.
GPU fixture success does not close matched loaded-world acceptance.

### Stage 6 source/model foundation (native owner activation remains open)

Registration now captures pack manifests/shader/include bytes for both hosts,
including .h headers. Pending renderer selection retains that captured root.
The shared model maps all26 stage names and Lighting-family injection and
preserves ownership and Depth restrictions; DX11's duplicate scanner is removed.
ShaderPackRegistry.GetCapabilities separates registration/support from actual
activation/production and explicitly reports Vulkan named-stage/same-draw
attachment execution as unavailable until those paths are implemented.

The native resource-free fullscreen branch was found to use RGBA8 regardless
of target format. Resource-free shaders now use the same format-aware pipelines
as bus shaders. Captured-pack HDR/negative-alpha execution and the full current
pack pipeline gate pass on a private isolated native build with synchronization
validation enabled and zero errors. Native17 tests pass without skips; the
actual-core scene/recreation gate passes329 checks on that binary. The compiler
gate passes39 checks, and generated GPU-owned stock vertex/pixel variants retain
stock provenance and identical SPIR-V for a comment-only source override.
Per-stage raster execution, full attachments and Windows live acceptance remain
open; source-model fixtures are not counted as stage execution proof.


### Vulkan owned-raster generations (stage 6, partial)

Anomaly now prepares detached raster generations for `Anomaly.CameraVelocity`,
`Anomaly.LinearDepth` (including HiZ) and `Anomaly.HistoryColor`. Sources and
includes are captured with pack identity, owned/game shader bytes, injection order and pack
defines. Overlapping overlays fail closed to stock; conflicting exclusive packs
are disabled as on DX11. A replacement of shared `Fullscreen.hlsl` affects every
owned raster consumer in the same transaction. The adapter explicitly converts
DX11 clip-space Y for Vulkan's positive viewport while preserving custom UVs. Native lit-mip blits
are outside this raster contract and do not execute arbitrary HistoryCopy code.

Preparation compiles and creates new pipelines without changing the active
programs. Before the frame snapshot, Anomaly rechecks registration revision,
device epoch and program identity, then activates the complete generation under
one lock. Compilation, native descriptor rejection or stale preparation retains
the prior valid generation. Unregister prepares stock restoration. Pipeline-only
retirement preserves shared output/history images and uses SE-Vulkan's existing
submission-completion retirement. Device recreation recompiles the captured
active sources and rejects prepared handles from the old epoch.

`ShaderPackRegistry.GetOwnedStageState(stage)` reports `RasterReplacementSupported`,
`RequestedGeneration`, `PreparedGeneration`, `ActiveGeneration`, `ProgramIds` and
`Error` for this Vulkan subset. Use `FullscreenPassRegistry.GetProgramState(id)`
for each listed program: `ActiveGeneration` identifies its generation, while
native execution counters/completion/current-frame production remain separate.
Preparation and activation do not imply execution. A null active generation
means the initial stock program, before any generation transaction.

The deterministic Vulkan fixture proves captured pixel replacement, named
vertex orientation at odd dimensions, injection/defines actually used by the
pixel shader, HDR/negative alpha, native execution, compile/binding rollback,
stale source/device refusal and stock restoration, with validation enabled and
zero errors. This is a partial execution gate. Complete stock-native stage
activation, reserved catalog bindings, same-invocation attachments and matched
loaded-world acceptance remain open; the aggregate named-stage capability
stays false.


Owned-raster replacement requires native feature bit18 in addition to bit13
retirement. Older hosts receive an explicit update refusal. ABI-1 packet layouts
are unchanged. Reloading identical captured sources coalesces to the active
pipelines. Shared-volume generated requests now capture each provider's declared
pack root (including sibling Inject/ headers), or its standalone directory;
active includes escaping capture are still refused by the compiler.

The final actual-core suite passes355 checks with synchronization validation
and zero errors. It executes real Fog/Clouds providers and the owned-stage
synchronous/pipelined replacement/rollback/restoration path with an outstanding
submission. A matched loaded-game DX11/Vulkan comparison is still required.


### Native atmosphere and foliage owners — focused stage6 gate

Native bits24/25 add the actual LUT/main/probe atmosphere modules and shared
foliage VS/two-GS/two-PS closure. The integrated170-check actual-core GPU gate
proves all eight modules, reserved resources from every shader stage, both probe
MRTs on all six faces, failed-candidate rollback, exact stock restoration and
completion-based pipeline/layout retirement with zero validation errors.
Aggregate named-stage and same-draw attachment support remains false. Remaining
stage6 owners/attachments precede stage7 and stage8, including matched Windows
loaded-world acceptance; native mip blits are not arbitrary HistoryColor execution.

The coordinated native binding transport accepts all87 current owner rows within
its90-row bound; an oversized packet leaves the copied rows unchanged. Eight
real older-bit23-host checks prove actionable bit24/25 refusal before compilation
or native candidate allocation. This is focused stage6 evidence.


### Native decal pair generations — focused stage6 execution gate

Native bit26 adds five captured stock VS/PS pairs (1024..1033), corresponding to
native decal roles1,2,3,7,8. A pair prepares and activates together; fragment
invocations carry the merged vertex/pixel resource contract. The original
indexed-volume invocation, dynamic b2 batch offsets, per-channel GBuffer masks,
read-only depth and transparent OIT outputs remain native-owned. Catalog requests
can explicitly replace material t5/t6; absent requests retain their stock inputs.
Pipeline/layout retirement follows the last completed submission. ABI-1 packet
layouts remain unchanged. The module/row transport bound is100 (92 actual binding
owners after shared-module aliases merge). Older hosts require coordinated bit26
support before pack compilation or native candidate allocation.

The focused98-check actual-core GPU gate proves all ten modules with independent
GBuffer and OIT output equations, vertex/pixel catalog inputs, dynamic b2, atomic
activation, failed-compilation/incomplete-pair rollback, exact stock restoration
and retirement. Each role produces exactly matching synchronous/pipelined output, with zero
core/synchronization errors.
The updated170-check environment gate passes with all92 binding-owner rows and
oversized-packet preservation. Ten real older-host checks cover bits24/25/26.
Native CTest executes16 passes plus its configured reference skip; managed net48/
net10 builds and focused CPU native-stage/binding contracts pass. The extended
full recreation gate now includes these fixtures but has not been rerun for this
batch. This evidence does not establish every legal shader interface, other native
stages, full attachment semantics or parity. Both aggregate named-stage and same-draw attachment flags
remain false. Stage7 and stage8 follow the remaining stage6 work.


### Native probe processing generations — focused stage6 execution gate

Native bit27 extends the generation transaction to captured probe-processing
owners1280..1283: prefilter, forward sky, six-MRT publication copy and stock-dormant
factor blending. Selected generations activate at one frame boundary and old
pipelines/layouts retire after completed submission. Native scope binds the
actual probe b0 and complete144-byte forward b2 (view, projection, sun, size)
when a replacement reads beyond the stock64-byte view. Stock uploads retain
the64-byte path.
Catalog t5..t9 overrides are explicit; unrequested stock input slots retain their
ownership, including the publication copy's sixth-face t5. Native mip/base-level
image copies remain separate from named shader execution. ABI-1 packets retain
their layouts; module/row capacity is104 with96 possible descriptor owners.

Installed DX11 MyEnvironmentProbe fixes BLEND_USING_FACTOR=false and invokes
EnvProbeCopy with blendStrength, so EnvProbeBlend can activate but has zero GPU
execution in that stock path. Its compilation/activation must never imply
production. This does not claim dispatch support for dormant EnvProbe compute
entry points or complete geometry/EnvProbe parity.

The focused326-check actual-core GPU gate passes independent per-face/per-mip
equations on all six probe cubes, exact synchronous/pipelined outputs, held-capture
execution status, frame-copied catalog changes, invalid compilation/MRT rollback,
exact stock restoration and retirement with zero validation errors. The existing
stock-probe regression passes427 checks with zero errors, and six real bit26-host
checks prove actionable bit27 refusal before compilation/allocation. Both managed
frameworks and the native build pass. The final combined496-check environment/probe gate passes all96 binding-owner
rows and oversized-packet preservation with zero errors. Native CTest and focused
CPU native-stage/binding contracts pass; the broader recreation suite includes
the new fixtures but has not been rerun for this batch.
Both aggregate named-stage and same-draw attachment capabilities remain false;
remaining stage6 work precedes stages7 and8, including Windows live acceptance.


### Native particle generations — focused stage6 execution gate

Native bit28 adds the complete particle closure1536..1545: eight compute variants
and the shared VS/PS render pair. A selected particle source rebuilds the entire
list-protocol family and activates it atomically at a frame boundary. Old
pipelines/layouts retire together after completed submission. Workgroup sizes,
64/464/4-byte native storage layouts, typed argument formats and stage-specific
inputs validate before activation. Stock buffer ownership and OIT/depth behavior
remain native. VS descriptor bindings retain their1024-offset relocation; merged
logical t5..t9 reservations and b6 extras bind the corresponding physical slots.
The module/row bound is114 with105 possible binding owners after aliases merge.

Direct nonzero dispatches report submitted participation separately from
completion. Indirect simulation/raster participation is credited only when
completed GPU argument readback proves nonzero X/Y/Z and index/instance counts,
with the original
frame and matching generation token. Empty work cannot advance those execution
records; stale generations cannot credit a newer program.

The checked40-byte ABI-1 particle readback request validates size/version, buffer
kind, aligned range and a64KiB bound outside recording. The diagnostic native
buffers stay renderer-owned; this adds no pack discovery or execution host.
Older hosts actionably refuse bit28 before compilation, candidate allocation or
readback symbol lookup. Existing packet layouts and DX11 callback signatures stay
unchanged.

The focused84-check actual-core GPU gate proves all eight compute modules with
independent GPU sentinel writes and the raster pair with expected OIT pixels,
VS/PS/compute resources, empty index/instance/X/Y/Z participation and recovery,
wrong workgroup/
stride/stage-input refusal, malformed readback size/version/range, rollback, exact
stock restoration and retirement. The existing216-check stock transparency and
particle lifecycle gate and updated170-check environment/105-row transport gate
pass, all with zero core/synchronization errors. Seven real previous-bit27-host
checks pass. Both managed frameworks, native CTest and focused CPU contracts pass.
The extended full recreation fixture compiles but has not been rerun for this
batch. Both aggregate flags remain false; this does not close all legal shader
interfaces, remaining native owners, same-invocation attachments or full parity.


### Native screen-shadow generations — focused stage6 execution gate

Native bit29 adds CSM evaluation1792 and optional per-view usage statistics1793
to the captured-source generation transaction. Each native owner receives its
actual frame/screen/cascade inputs, shared reserved t5..t9 and b6 extras, fixed
16x16x1 dispatch contract and native output. Activation moves complete pipeline/
layout bundles at a frame boundary; retired generations wait for completion.
ABI-1 module/state packets remain unchanged. Capacity116 includes107 possible
binding-owner rows. Host capability checks now follow ascending feature versions
so global defines report the first unavailable owner contract consistently.

DXC encodes HLSL unorm<float> storage as R32f. The derived CSM executable adapts
that storage-image type to the owning R8_UNORM target and adds the required
StorageImageExtendedFormats capability, already enabled by SE-Vulkan. Original
authenticated shader artifacts remain intact. Other storage formats are refused.

The43-check actual-core GPU fixture passes independent CSM UNORM equations, exact
per-cascade usage counts, frame catalog updates, atomic activation, synchronous/
pipelined output, inactive usage status, compile/workgroup rollback, exact stock
restoration and retirement with zero core/synchronization errors. Native and
managed net48/net10 builds pass. Six real bit28-host refusal checks, the170-check environment/107-row transport
gate and36-check stock shadow-occlusion regression pass with zero validation
errors. Native CTest executes16 passes plus the configured reference skip;
focused CPU contracts and the developer canvas check pass. The extended full
recreation fixture compiles but has not been rerun for this batch.
This covers two screen-space compute owners; shadow geometry, other Shadows
sources and full same-invocation attachments remain separate stage6 work. Both
aggregate capability flags remain false. Stage7 and stage8 follow stage6.


### Shared native source dependencies — focused stage6 execution gate

The device-free source transaction selects native consumers by include dependency,
including dependencies across named-stage families. Native catalog rows retain
their first three reflection fields and append authenticated stock dependency
paths. Core also walks copied source includes after publishing every unique
override, so replacement programs can introduce dependencies on other packs'
headers or entry-point sources. Relative, transitive, quoted/angle includes and
line continuations are resolved; comments and cycles do not invent dependencies.
Macro includes and older three-field catalog rows choose conservative selection.
Competing overrides retain existing conflict handling. Generic shared header
extensions are eligible even outside the currently mapped native stage folders.
The generation fingerprint contract advances to the captured-include-closure
revision; complete selected closures still activate and roll back atomically.

The21-check actual-core GPU gate changes only Shadows/Csm.hlsli: all authenticated
CSM consumers in shadows, lighting, decals and particles join the same generation,
selected shadow pixels change to independently expected values, unrelated tone
mapping stays untouched, and synchronous/pipelined output agrees. Header reload,
invalid-header rollback, exact stock shadow/HDR restoration and dependent-layout
retirement pass with zero core/synchronization errors. The170-check environment
integration also passes; focused CPU contracts cover dependency selection and
older catalog fallback. Managed net48/net10 builds pass. Native ABI/capability
bits are unchanged. This covers the current native owner catalog; missing owner
families and legal interface/attachment contracts remain stage6 work. Both
aggregate capability flags remain false. Stages7 and8 remain ordered afterward.


### Native OIT resolve generation — partial stage6 owner

Native bit30 adds owner2048 for `Transparent/OIT/Resolve.hlsl`. Anomaly captures
and selects the source transaction; SE-Vulkan compiles its authenticated stock
variant or derived replacement, prepares both HDR pipelines and the reflected
layout together, activates at the frame boundary and retires the replaced bundle
after submission completion. Existing ABI-1 packets stay unchanged. The module
and binding-row bound is117, with108 possible native descriptor owners. Older
hosts refuse OIT overlays, Transparent injection and global defines actionably.

The actual weighted-OIT publication invocation supplies accumulation/coverage
inputs, reserved catalog t5..t9, frame b0, extras b6 and stock samplers. It preserves
native blend/raster semantics. Completed execution is reported only when the
native transparency scope actually draws; activation alone is insufficient.
The current builder requires a float4 target preserving blend alpha. Wider legal
interface compatibility, transparent geometry VS/PS and arbitrary attachment
outputs remain stage6 work; this owner does not complete the Transparent family.

The32-check actual-core GPU fixture proves independent HDR blend values, catalog
clears and extras, both HDR formats, exact synchronous/pipelined agreement,
no-work execution honesty, failed compilation/layout rollback, exact stock
restoration and completed retirement, with zero core/synchronization errors.
Six actual previous-bit29-host refusal checks pass. Native mip blits remain copy
operations, not arbitrary HistoryColor shader execution. Both aggregate named-
stage and same-draw-attachment capabilities remain false. Finish remaining stage6
owners and full same-invocation attachments before stages7 and8, including the
required matched Windows loaded-world acceptance matrix.

The170-check environment integration passes with108 binding-owner rows,117-row
acceptance and118-row rejection preserving copied rows. The216-check stock
transparency/particle lifecycle regression and native CTest (16 executed passes,
one configured reference skip) pass. Both managed target frameworks and focused
CPU native-stage contracts pass. The full recreation fixture compiles with this
new fixture; the broad combined recreation suite was not rerun for this batch.


### Fixed-layout stock-world generations — partial stage6 owner contract

Native bit31 adds logical VS/PS owner IDs `4096 + 2*pipeline` and
`4097 + 2*pipeline` for the initialized stock world pipeline catalog. Existing
ABI-1 module/state packets remain unchanged; the checked generation module bound
is8192. The signed managed flags retain all32 native feature bits. Previous
bit30 hosts refuse geometry overlay, injection and global-define requests before
compilation or candidate allocation. Depth injection remains forbidden by the
shared parser; GBuffer replacement still requires its existing exclusive owner.

The catalog authenticates captured and supplemental variants and selects by
actual GBuffer/Depth/Forward/Highlight pass header, avoiding ambiguous shared
material entry-point ownership. Derived supplemental requests preserve their
stock defines and authenticate installed sources. Native generic-instance tint
adaptation remains present in active and restored variants. VS/PS pairs enter the
same source transaction and activate atomically at a closed-frame boundary.
The builder prepares complete main/cascade/spot bundles; replaced resources
retire after submission completion. Actual world, probe, highlight and spotlight
draw sites report their selected owner. Parallel geometry recording updates
production counters atomically. Stock initialization invalidates obsolete
geometry generation metadata.

The focused57-check actual-core GPU gate proves replacement pixels for rigid,
skinned, generic-instanced, cube and deformed-cube classic material streams in
both recording modes; atomic ten-module activation, empty-frame honesty, failed
compilation rollback, exact stock restoration including generic tint, and
completed retirement pass with zero core/synchronization errors. Its private
catalog deliberately isolates those five streams. It is not evidence that every
cataloged shader executes. Eight real previous-host refusal checks pass.

Geometry currently retains the fixed stock descriptor interface; additional
reserved catalog resources and the stage-specific geometry constants still need
integration. The DX11 geometry constants must not be replaced by fullscreen
extras merely because both use register b6. Native-cell and GPU-owned geometry
owner generations are described below; remaining legal interfaces and
same-invocation attachment outputs remain required stage6 work. Both aggregate capability flags stay false.
Native mip blits remain copies, not arbitrary HistoryColor shader execution.
Stages7 and8 follow stage6, including the required matched Windows live matrix.


The separate59-check unfiltered GBuffer family gate uses the productioncatalog:
1,120 material/decal/fade/voxel modules activate atomically, Depth stays untouched,
the five drawn streams retain expected pixels, rollback and exact stock/tint
restoration pass, and retired bundles collect with zero validation errors.
The complete stock-world catalog contains1,316 modules. Family-wide construction
and activation do not prove execution for every shader or geometry path.

The71-check actual-core raw Depth replacement gate proves independent depth
values in real cascade and spotlight invocations across the five classic streams,
exact recording-mode agreement, atomic pair activation, rollback, exact stock
restoration and retirement. Native depth-owner replacement invalidates cached
cascades and frozen spot maps; a zero caller redraw mask cannot suppress required
shader-change recovery. Ordinary unchanged shadow-cache reuse reports its prior
production frame and no new shader execution. Depth injection remains forbidden,
and attachment extensions do not gain permission to change stock Depth passes.

The final focused GBuffer gate has57 checks. Managed net48/net10 builds, focused
CPU native-stage contracts, native CTest (16 executed passes plus one configured
reference skip) and canvas TypeScript pass. Full recreation source now includes
both new fixtures and builds; that broad combined suite has not been rerun.
Remaining stage6 contracts and the mandatory stages7/8 Windows live acceptance
remain open; both aggregate capability flags remain false.


### Native-cell and GPU-indexed geometry generations — partial stage6 owner contract

An optional size/version-checked extension leaves the ABI-1 packets unchanged.
`sevk_native_owner_capabilities` fills a 24-byte record: flag1 native cells,
flag2 GPU NEW, flag4 GPU OLD, a module bound of 8192 and 117 binding rows.
`sevk_prepare_native_generation` takes a 32-byte request with the existing
32-byte module records. Its flag1 marks stock restoration; unknown flags, wrong
sizes, unknown ABI and empty module sets are refused before candidate
allocation. Owner spaces alternate VS/PS: native cells `[16384,16440)`, GPU NEW
`[32768,32824)` and GPU OLD `[49152,54464)`. Only initialized pipelines appear
in the catalog. Anomaly requires flags 1/2/4 for geometry overlay, injection and
define requests. It closes VS/PS pairs in each owner space and refuses older
hosts before compilation.

Detached builders keep the actual stock layouts, rasterization and admission
rules, and build back/no-cull pairs. One generation switches the live classic,
cell and GPU handles at a closed-frame boundary. Replaced handles retire after
timeline completion, and Depth-owner changes invalidate cascade and spotlight
caches. GPU-indexed production is not inferred from CPU-recorded
`vkCmdDrawIndexedIndirectCount`. A trusted compute reduction reads the
immutable submitted count and argument buffers. It reads back one word per row
after completion, and zero counts, index counts or instance counts claim nothing.
Conditional spotlight scopes attribute classic, cell and GPU draws to the GPU
visibility word. The condition is thread-local to the spot recorder, so
concurrent cascade and pass workers never inherit it. Generation tokens keep
work from being credited to a newer replacement. Pack-only proof resources
retire when the last tracked generation returns to stock.

The device-local actual-core gate passes 92 checks with synchronization
validation and zero errors. It cataloges all 28 cell variants and initialized
GPU NEW/OLD pairs, and runs a four-path mixed frame with expected GBuffer and
shadow pixels in both recording modes. It covers hidden-work honesty, atomic
activation, compile-failure retention, ABI refusal, exact stock restoration and
retirement. Its conditional spotlight lane covers classic, cell and GPU NEW
depth owners in one GPU-conditional scope. It checks independent pixels and
per-owner attribution after a caster change. A condition-false frame is
CPU-recorded with no claimed production, and a forced redraw reports fresh
production. Eight older-host refusal checks pass. Those cataloged cell variants
are construction evidence, not per-path execution evidence. Reserved geometry
bindings, stage-specific constants, transparent/billboard owners, legal
interface widening and same-invocation attachments remain open. Both aggregate
capability flags stay false, and stages 7 and 8 still follow.


### GBuffer velocity contract on native owners — first admitted path

DX11 binds catalog t5..t9 and the b6 extras only for Lighting, Post.Tonemap,
Post.HBAO, Transparent and Atmosphere. It binds nothing of that kind for
GBuffer, Depth, Forward or Highlight. So skipping native geometry owners in
`StageBindingOwners` is parity, not a gap. DX11's real geometry reservations
are the `ANOMALY_VELOCITY` contract, applied to GBuffer permutations only
(never Depth):

- VS b6, the 240-byte `AnomalyVelocity` block;
- VS t15 previous instances and VS t16 previous bones;
- PS b7, the 16-byte probe;
- Target3 velocity, written by the same draw.

The four hash-checked Keen patch transforms now live in the shared
`GBufferVelocitySource`. DX11 calls them unchanged. Vulkan uses them with
`ANOMALY_SPIRV`, which drops DX11's internal SV_Target7 audit. Vulkan has one
register namespace per descriptor set, so those VS registers would alias
Keen's PS b6/t15 and forward t16. `Anomaly.hlsli` therefore relocates them to
set 2 under `ANOMALY_SPIRV`. As on DX11, a pack overlay of the same key wins.
A GBuffer PixelStage overlay must start from the velocity-aware source on both
hosts.

Native owner flag8 requires `VK_EXT_dynamic_rendering_unused_attachments`.
Stock pipelines keep three attachments, and only owner pipelines add Target3.
`CompileNativeOwnerGenerationWithContracts` sends the contract bit, and Anomaly
sends it only for generations that change sources. Admitted owners are the
classic GBuffer pipelines 0..19: rigid, skinned, generic-instanced, cube and
deformed-cube streams. Keen compiles those without `USE_SIMPLE_INSTANCING`,
so their `local_matrix` comes from the object block. Skinned velocity is
therefore rigid-body: bone animation is not part of DX11's classic velocity,
and the replay never covered skinned draws at all. DX11 therefore gives them
the per-draw currToPrev rows and never reads t15; only `USE_SIMPLE_INSTANCING`
(the Stage 2/GPU NEW path) indexes t15. Per-instance changes are not part of
DX11's velocity for these streams. Admitted owners compile with
`ANOMALY_VELOCITY`, leave indirect grouping, and get a set-2 layout with real
per-draw writes:

- b0 holds the replay's view-projections, the render size and the
  old-pipeline currToPrev rows, at a dynamic offset;
- b1 is the zero probe;
- t15 and t16 are bound storage buffers.

The main GBuffer scope adds the motion target only on frames with admitted
draws. The replay then loads that target and skips those draws. Without
history, owners write zero, as the replay does.

The 157-check actual-core gate (`--gbuffer-velocity-only`) passes with
synchronization validation and zero errors, on all five classic streams:

- Stock replay matches the analytic -1.067 px motion.
- Owner-emitted Target3 matches both the analytic and replay values.
- A pack-written (3, -2) reaches Target3, which the replay cannot produce.
- The skinned owner emits the analytic rigid-body motion and pack-written velocity.
- Results agree across recording modes.
- Failed compilation keeps the previous generation.
- Unregistering restores exact stock material and motion bits.
- Retirement completes.

GPU NEW GBuffer owners compile `USE_SIMPLE_INSTANCING`, which is where DX11
reads t15. One indirect draw covers many objects there, so b0 is per frame:
view-projections, size, PrevCount, HasHistory and the camera delta, with
InstanceBase 0. Vulkan's `SV_InstanceID` already indexes the global instance
buffer. The GPU adapter replaces the t15 `StructuredBuffer` with a lookup:
each instance resolves to its actor through the culled references, then reads
that actor's previous rows, which the indexed replay keeps. The replay
prepares that history once, before the GBuffer, and then skips owner batches.
Admission requires the variant's own `USE_SIMPLE_INSTANCING`. t16 has no
Vulkan consumer: GPU skinned sources keep their classic fallback, where DX11
also ignores bones. The 40-check `--gbuffer-velocity-gpu-only` gate passes with
zero errors:

- Owner motion matches the analytic -0.64 px and the indexed replay.
- Pack-written (3, -2) reaches Target3 through indirect draws.
- Results agree across recording modes.
- Unregistering restores exact stock bits.

Native-cell GBuffer owners also compile `USE_SIMPLE_INSTANCING`. They draw
each group with firstInstance 0, so b0 is per group and
`InstanceBase = history_offset / 64`, matching DX11's
`t15[SV_InstanceID + InstanceBase]`. t15 is the current slot's per-instance
history stream; its unused bytes 48..63 now carry flags.x = 1 for a known
previous world. The replay skips owner groups. The 40-check
`--gbuffer-velocity-cells-only` gate passes with zero errors: owner motion
matches the analytic -0.64 px and the cell replay, pack-written (3, -2)
reaches Target3, and unregistering restores exact stock bits.

GPU OLD owners compile Keen's classic streams, so DX11's contract for them is
the per-draw b6 currToPrev rows and `HasPrevWorld`. One indirect draw covers
many objects, so the GPU adapter renames those cbuffer members and fills
shader statics per draw instead. It computes `Prev * Invert(Curr)` from the
draw's actor history row (`draw.reserved`) and the current object rows that
the culling compute writes. The 240-byte cbuffer layout is unchanged, and the
replay skips owner batches. Admission covers classic GBuffer pipelines below
20; GPU skinned sources stay on their fallback. The 40-check
`--gbuffer-velocity-gpu-old-only` gate passes with zero errors: owner motion
matches the analytic -0.64 px and the indexed replay, pack-written (3, -2)
reaches Target3, and unregistering restores exact stock bits. The gate drives
the rigid stream; instanced and cube GPU OLD streams share the same code.

Fade-bank variants of the classic and GPU OLD streams share their base
stream's per-draw currToPrev rows; the shader applies the fade itself. Blended
static decals are also admitted. DX11 binds Target3 as an RG replace (blend
off, RG write mask) for every GBuffer draw, decals included. So Vulkan decal
owners add a fourth attachment with exactly that state, and their three
GBuffer blends are unchanged. The classic gate now covers 197 checks:

- the fade-bank owner (2562) matches its replay;
- the blended-decal owner (128) writes the analytic -1.067 px that the replay
  never covered;
- pack-written velocity and exact restoration hold on both.

Voxel GBuffer shaders (classic 20/21) are not native geometry owners on Vulkan:
they come from the voxel shader library, so packs cannot yet replace them
through native generations, and there is no voxel owner to emit velocity. DX11
gives voxels camera-only velocity from the pass-begin block. Adding voxel
owners is an owner-family gap, not velocity work.

Pack GBuffer attachments now follow DX11's same-invocation contract on these
owners. Anomaly generates `GBufferAttachmentDefs.hlsli` and
`GBufferAttachmentFields.hlsli` from the live requests, gated on
`ANOMALY_VELOCITY` so only GBuffer owners see them, as DX11 adds the macros
only to GBuffer permutations:

- `ANOMALY_ATTACH_<NAME>` and `_TARGET` with fields at SV_Target4+slot;
- packed `GBuffer1.a` via `ANOMALY_PACK_GBUFFER1A`, `ANOMALY_ATTACH_PACKED_NAME`
  and the pack's `AnomalyGBuffer1A` override.

It declares each slot's format to the host with
`sevk_declare_gbuffer_attachment`, so a pack needs no Vulkan-only replay
shader. Owner pipelines add those targets, with UNDEFINED for unused slots.
The main GBuffer scope binds declared attachment images at index 4+slot. The
replay pass loads them and skips owner draws, and decal owners write them as
replace. The 33-check `--gbuffer-attachments-only` gate passes with zero
errors:

- an R32_UINT attachment carries 777 from the owning invocation (uncovered
  pixels stay cleared);
- `GBuffer1.a` carries the pack's 0.5 (stock writes 63/255);
- velocity stays correct beside both;
- recording modes agree, and unregistering restores exact stock bits.

The Vulkan-only `GBuffer/<name>.hlsl` write shaders still serve non-owner
draws.

Native-cell, GPU NEW and GPU OLD owners write the same attachments from their
own invocations. Their draws record inside the same main GBuffer scope, so
they need no separate wiring. The velocity lanes for these paths
(`--gbuffer-velocity-cells-only`, `--gbuffer-velocity-gpu-only`,
`--gbuffer-velocity-gpu-old-only`) each add 15 attachment checks, for 55
each. On each path:

- objectid is 777 at SV_Target4;
- `GBuffer1.a` is 127 against stock 63;
- velocity stays correct;
- recording modes agree;
- unregistering restores exact GBuffer1 and motion bits.

A declared-only slot has no indexed replay shader. The indexed replay now skips
it instead of failing with "indexed attachment geometry class unavailable"
once the owning pack unregisters.

Still open on this contract:

- voxel owners (not yet native geometry owners);
- GPU OLD instanced/cube execution fixtures;
- probe modes;
- attachment fixtures on native-cell/GPU owners (they share the format helper).

Both aggregate capability flags stay false.
