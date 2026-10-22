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


### Native catalog/extras bindings - bit22

The earlier unavailable-native-binding status above is superseded for the
existing native lighting, postprocess and HBAO owners. Core reservations now
freeze before callbacks and resolve image publications afterward. Native rows
copy owner/frame IDs, reserved t5..t9 image IDs and freshness/history metadata;
malformed, duplicate and stale rows refuse transactionally. Attachment slots
take precedence. A stock t5 input remains unchanged unless explicitly reserved.

b6 uses the fixed shared 320-byte extras layout, with full raster dimensions on
quarter-size HBAO and actual current velocity availability independent of history
validity. Lighting owns detached custom layouts only when extra bindings require
them; pipelines/layouts retire together. Base-only restoration reuses stock
layouts/descriptor sets. DX11 lighting hooks now use their actual Dir/Point/Spot
stage names so specific requests match on both hosts.

Device nullDescriptor support supplies true zero-valued missing typed resources,
including zero image dimensions. 2D/3D/array/depth/numeric mismatches fail closed
and are counted. Structured buffers upload through the protected frame slot.
The shared facade's typed GetNativeState separates transport/device support,
registered reservations, queued owner/frame and catalog descriptor records.
Shader variant execution/frame/completion queries remain the execution evidence.

Persistent targets expose RequiresCurrentFrame=false; ordinary renderer products
remain fresh-only. Private global target clears run before native owners under
bit22, including FP16 alpha -1, while explicit callbacks retain their phases.
Reserved historyColor requires an immediately preceding owned producer and its
reference freezes before current commands. Unsupported hosts/devices refuse new
resource contracts and retain the prior valid generation/stock.

The focused resource gate passes194 checks with validation enabled and zero errors:
all four lighting variants, four compute tone permutations, both HDR formats,
HBAO clamp/border, true null resources, HDR/negative-alpha histories, first-use
clears, exact history rejection/recovery, callback mutation and stock restoration.
Independent DX11 WARP/null and WARP+HAL packed-HDR probes supply the numeric oracle.
The combined actual-core gate passes664 checks, and the separate native family
gate passes382, including all82 postprocess/lighting variants and unchanged
stock highlights. Both report zero core/synchronization errors. Other native owners,
vertex/geometry contracts, complete same-draw attachments, stage7 and automated
stage8 remain required; aggregate named-stage/same-draw capabilities stay false.


Reserved historyColor rows now share the fullscreen/portable frame snapshot and
validate producer identity, exact age, resource epoch and render dimensions.
HistoryCopy records after all late consumers and phase1 callbacks. This preserves
named shader replacement and keeps capture at raster size while ordinary
AfterUpscale remains output-sized. The focused125-check actual-core history gate
passes with zero core/synchronization errors; the final combined gate passes891
checks with zero errors.


Classic fullscreen optional-resource fidelity now has separate native bit23 and
device nullDescriptor qualification. Native-owner bit22 alone does not promise
classic fullscreen null dimensions. Query ClassicNullResourcesSupported on the
shared ShaderApiCapabilities; named-stage and same-draw aggregate support remain
partial. See Buffer-catalog for resource types and coordinated-update refusal.


### Environment-family native owners (focused execution gate, 22 October)

Native bit24 adds captured atmosphere compute/main-view/probe owners512..514 to
the shared preparation/activation transaction. Changed compute generations dirty
cached LUTs while preserving their actual image layout and synchronization. Probe
atmosphere retains its two-target format and scope-specific stock inputs. Native
bit25 adds foliage768..772: one shared vertex module and two geometry/pixel pairs
form an indivisible generation closure. Each raster invocation binds the merged
module resource contract; execution counters identify every participating stage.
Layouts and pipelines retire together after their last completed submission.
Catalog t5..t9 and b6 extras remain shared host contracts. ABI-1 packets retain
their layouts. Older hosts receive coordinated-update refusal.

The integrated environment owner batch passes170 actual-core GPU checks with
core/synchronization validation enabled and zero errors. Independent outputs cover
LUT/main-view atmosphere, both probe MRTs on all six faces, and shared foliage VS,
each GS and each PS. Vertex/geometry/pixel-only catalog resources, first-use s1,
probe-scope constants, immutable extras, both HDR formats, synchronous/pipelined
execution, incomplete closure/interface/compile rejection, exact stock restoration
and completion-based retirement are covered. Managed net48/net10 and native builds
pass. The previous combined998 gate predates this batch; it is not expanded proof.
Aggregate named-stage/attachment capabilities remain false. Remaining stage6
owners/attachments precede stage7 and stage8, including matched Windows loaded-world
acceptance. Native mip blits do not establish arbitrary HistoryColor execution.

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
completed GPU argument readback proves nonzero groups/indices, with the original
frame and matching generation token. Empty work cannot advance those execution
records; stale generations cannot credit a newer program.

The checked40-byte ABI-1 particle readback request validates size/version, buffer
kind, aligned range and a64KiB bound outside recording. The diagnostic native
buffers stay renderer-owned; this adds no pack discovery or execution host.
Older hosts actionably refuse bit28 before compilation, candidate allocation or
readback symbol lookup. Existing packet layouts and DX11 callback signatures stay
unchanged.

The focused65-check actual-core GPU gate proves all eight compute modules with
independent GPU sentinel writes and the raster pair with expected OIT pixels,
VS/PS/compute resources, empty indirect participation/recovery, wrong workgroup/
stride/stage-input refusal, malformed readback size/version/range, rollback, exact
stock restoration and retirement. The existing216-check stock transparency and
particle lifecycle gate and updated170-check environment/105-row transport gate
pass, all with zero core/synchronization errors. Seven real previous-bit27-host
checks pass. Both managed frameworks, native CTest and focused CPU contracts pass.
The extended full recreation fixture compiles but has not been rerun for this
batch. Both aggregate flags remain false; this does not close all legal shader
interfaces, remaining native owners, same-invocation attachments or full parity.
