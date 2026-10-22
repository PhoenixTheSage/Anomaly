# Named stages

Public API is semantic names, not 215 Keen paths. Implemented in `ClientPlugin.Shaders.ShaderStages`. After apply, Anomaly compiles a sentinel for each live named stage and rolls back the pack that breaks it.

> **Caution — Depth.** Depth replacements must stay 0-target. A fourth MRT on Depth breaks shadows. Inject into Depth is rejected.

## Geometry

| Stage | Keen mapping | Notes |
|-------|--------------|-------|
| GBuffer | Shared GBuffer pass + `GBufferWrite` / `GBuffer.hlsli` | Velocity Target3 lives here. Not a Materials fork. |
| Depth | Depth pass stages | No extra MRT. |
| Forward | Probe / far forward | |
| Highlight | Selection outline | |
| Transparent | OIT pass + resolve | |
| TransparentForDecals | Glass receiving decals | |

## Lighting and post

| Stage | Keen mapping |
|-------|--------------|
| Lighting.Dir / .Point / .Spot | Deferred lighting programs |
| Post.Tonemap | `Postprocess/Tonemapping/Main.hlsl` |
| Post.HBAO | `Postprocess/HBAO/*` |
| Post.SSAO | `Postprocess/SSAO/Ssao.hlsl` |
| Post.Bloom | `Postprocess/Bloom/*` |
| Post.FXAA | `Postprocess/Fxaa.hlsl` |
| Post.EyeAdaptation | `Postprocess/EyeAdaptation/*` |
| Post.Luminance | `Postprocess/LuminanceReduction/*` |
| Post.ChromaticAberration | `Postprocess/ChromaticAberration/*` |

## Extra Keen stages

| Stage | Keen mapping | Sentinel |
|-------|--------------|----------|
| Shadows | `Shadows/Shadows.hlsl`, `Csm.hlsli`, … | `cs_5_0 Shadows.hlsl` |
| Atmosphere | `Transparent/Atmosphere/*` (Common wrap + extras at t6) | `ps_5_0 AtmosphereGBuffer LQ` |
| Decals | `Decals/Decals.hlsl` | vs + ps, no `RENDER_TO_TRANSPARENT` |
| GPUParticles | `Transparent/GPUParticles/*` | ps `STREAKS=0;LIT_PARTICLE=0` |
| EnvProbe | `EnvProbe/*` | ps `EnvProbeBlend.hlsl` |
| Foliage | `Foliage/*` | ps `Foliage.hlsl` |

Decals and foliage include `GBufferWrite`. Anomaly does not add `ANOMALY_VELOCITY` on those compiles (no `RENDERING_PASS`). Transparent, glass / holo / shield, foliage, and deferred decals never write Target3; camera fill already covers sky. Those materials are the leftover coverage holes — near-future work in [Docs/ROADMAP.md](../Docs/ROADMAP.md#slice-m--non-gbuffer-velocity-coverage-near-future) Slice M.

## Anomaly-owned stages

| Stage | Files |
|-------|-------|
| Anomaly.CameraVelocity | `CameraVelocity.hlsl`, `Fullscreen.hlsl` |
| Anomaly.LinearDepth | `LinearDepth.hlsl`, `HiZDownsample.hlsl`, `Fullscreen.hlsl` |
| Anomaly.HistoryColor | `HistoryCopy.hlsl`, `Fullscreen.hlsl` |
| Anomaly.LitMips | `HistoryCopy.hlsl` + `GenerateMips` of this-frame `LBuffer` |

## Vulkan native postprocess generations (partial stage 6)

Coordinated Anomaly/SE-Vulkan hosts with native feature bit19 prepare detached
stock-postprocess pipeline generations. Anomaly remains the pack host; SE-Vulkan
owns compilation, native activation and completion-based retirement. The
renderer invokes preparation before postprocess constant configuration and the
immutable frame snapshot. A source revision or device mismatch discards both
owned-raster and native candidates; a failed native commit retains both previous
generations and the next attempt rebuilds discarded owned candidates.

Postprocess entry-point overlays, include-only overlays, injections and legal
boolean defines use captured game/pack bytes through the existing authenticated
compiler. Canonical header overlays rebuild their named family; uncatalogued
shared postprocess headers conservatively rebuild all mapped families. HBAO
negative-border variants belong to the same transaction. Conflicting overlay
claims fall back to stock. Failed compilation, unsupported descriptors, invalid
compute workgroups or an incompatible fragment interface refuse the candidate.
Unregister restores stock. HDR format changes invalidate preparation even when
registration is unchanged; identical source/format generations are reused.

`ShaderPackRegistry.GetNativeStageState(stage)` separates requested and prepared
identities from per-variant `ActiveToken`, `ActiveGeneration`, `Executions`,
`LastProducedFrame` and `LastSubmissionCompleted`. Registration, compilation and
activation are not execution evidence. Existing ABI-1 packets are unchanged;
new module/status transports validate size/version and older hosts receive an
explicit update refusal. Host constant packing remains the stock layout;
immutable derived metadata is published separately at activation.

This is an initial native-postprocess owner implementation, not complete named
stage parity. Extra catalog descriptors remain unsupported on these owners.
Geometry, lighting, transparency, shadows, atmosphere, decals, particles,
probes, foliage, complete reserved bindings and same-invocation attachments
remain open. Aggregate named-stage and same-draw attachment capabilities stay
false. Matched Windows loaded-world visual/temporal/performance acceptance,
stage7 HDR/display integration and stage8 lifecycle qualification remain open.

Native postprocess evidence now includes398 actual-core checks and a separate
380-check native family gate, both with synchronization validation and zero
errors. The family gate activates all78 catalogued postprocess variants under
one authenticated generation, verifies each executes, and retains the existing
stock pixel/DRS/odd-size/HDR checks. A resource-free HBAO coarse replacement
produces quarter AO in all32 clamp/border permutations and restores exact stock
AO. When reflection proves no quarter-depth texture is read, the authenticated
base variant serves both modes; sampled HBAO variants retain the authenticated
border transform and its refusals for incompatible fetch rewrites. This is
execution coverage for the current native postprocess catalog, not every
advertised named stage or unrestricted catalog bindings.

## Vulkan deferred lighting generations (partial stage 6)

Native feature bit20 adds the four deferred lighting fragment variants to the
same captured-source transaction as native postprocessing: directional, tiled
point, spot and shadowed spot. IDs256–259 extend the existing size/version-checked
module/status transport without changing ABI-1 layouts or reflection method
signatures. `Lighting` family injections precede stage injections; shared lighting
headers rebuild lighting owners. Anomaly remains the sole pack host. The native
compiler uses authenticated stock inputs and the existing variant defines.

One native batch builds every selected lighting/postprocess pipeline before any
activation. Compilation, descriptor/interface rejection, stale source/device
state or a changed HDR format retains the prior generation. Both HDR pipeline
formats are prepared. Replaced pipelines retire after submission completion;
renderer-owned lighting descriptor layouts are shared and remain renderer-owned.
Unregister restores stock through the same transaction. Exclusive conflicts are
resolved before capability checks, so inactive conflicting owners retain stock.
Active lighting requests and global pack defines receive an actionable bit20
refusal on older hosts, before shader assets or GPU work are needed. Native named
variants no longer inherit the owned camera-velocity `ANOMALY_COMPOSITE` define;
explicit pack defines retain their existing boolean semantics.

`ShaderPackRegistry.GetNativeStageState("Lighting")` reports all four variants;
individual `Lighting.Dir`, `.Point` and `.Spot` queries remain available. Each
variant now exposes `Profile` alongside source, active identity and execution/
completion/frame state. These variants are `ps_5_0`. Legacy diagnostic lighting
replacement clears the native generation identity, preventing stale active
fingerprints from appearing in the status query.

This slice replaces/injects fragment programs within the existing native owners.
Lighting vertex/geometry overrides, complete reserved catalog bindings and
same-invocation attachment output remain open, along with the other native
owners. Aggregate named-stage and same-draw attachment capabilities remain false.
The full actual-core gate passes434 checks with zero core/synchronization
validation errors, including expected intermediate HDR pixels for all four
lighting variants, family/stage injection, shared-header reload, pipelined
replacement, HDR changes, mixed lighting/postprocess rollback, late batch failure
cleanup, exact stock restoration and completion-based retirement. Matched
loaded-world DX11/Vulkan visual, temporal and performance acceptance remains open.

The final focused actual-core rerun passes104 checks, including the define-scope
shader guard and explicit fragment profiles. The separate382-check native family
gate executes all82 selected postprocess/lighting variants and retains stock pixel
checks, with zero validation errors. These are deterministic GPU gates; loaded-world
acceptance and the remaining native contracts are still required.


### Shared binding facade and early products

The core now exposes ShaderBindRegistry with DX11's register constants and
RequestSrv(string, string, int = -1) reflection signature. Reservations made
before renderer selection replay into the DX11 companion. Both adapters use
one device-free reservation model. Automatic registration is idempotent; a
failed move into an occupied slot retains the original request. Automatic
lighting/post slots are t6–t9; atmosphere keeps engine t5 and velocity t6,
assigning extras at t7–t9. Velocity remains at its fixed slot. Snapshots are
immutable and copied only when reservations change.

Vulkan native owner descriptor bindings remain unavailable. The facade retains
requests and reports this explicitly; registration does not prove native binding
or execution. Typed/null resources, b6 extras on every native owner and
same-invocation attachments remain open stage-6 contracts.

Native bit21 moves only CameraVelocity, LinearDepth, HiZ and ReactiveClear
production after GBuffer/object-motion/decal work and before AO/lighting. Public
BeforeFullscreen/AfterFullscreen phases and marks1–6 stay unchanged; HistoryCopy
stays in its ordinary slot. ABI-1 layouts are unchanged. Old hosts receive no new
flags. Public replay cannot invoke the internal mark. Missing optional legacy 2D
inputs now sample explicit zero including alpha; required extended inputs still
refuse admission.

The active goal waives Windows live acceptance. Deterministic execution proof
for every legal contract, automated lifecycle/temporal/performance qualification,
documentation and compatibility checks remain required. Loaded-world Windows
comparison is tracked separately.
