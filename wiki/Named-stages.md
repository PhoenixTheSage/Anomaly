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
