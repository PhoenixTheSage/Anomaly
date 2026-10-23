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

GPU OLD generic-instanced, cube and deformed-cube owners (pipelines 2, 3 and
4) are covered too. They read their instance records from the source buffer,
and their velocity comes from the same per-draw currToPrev rows as rigid
draws. The GPU OLD lane now runs each stream against its own source record
and checks that:

- velocity matches the analytic value and the stock replay (-0.64 px);
- objectid 777 and `GBuffer1.a` 127 come from the owning invocation;
- recording modes agree;
- pack-written (3, -2) velocity reaches Target3;
- unregistering restores exact stock bits.

The lane passes 140 checks, and no native change was needed.

### Transparent glass and holo owners

Stock glass and holo transparent geometry now has native owner generations,
advertised by native owner flag16 (ids 2304-2343). Owner i's VS is 2304+2*i
and its PS is 2305+2*i:

- i<10 is OIT glass pipeline i, keyed `Geometry/Passes/Transparent/*Stage.hlsli`;
- i>=10 is decal-depth glass pipeline 50+i, keyed
  `Geometry/Passes/TransparentForDecals/*Stage.hlsli`.

The catalog lists all 20 pairs, and each stock artifact compiles through its
pass `Declarations.hlsli`. The DX11 overlays, Transparent injections and
header dependencies select a pair as they do for other families.

Owners keep the stock glass descriptor contract. They also admit the pack
extras (b6) and the reserved catalog resources (t5-t9). Activation swaps the
no-cull, culled and depth-only pipelines into the stock slots. OIT glass and
the glass decal depth layers write each owner's reflected bindings, including
dynamic offsets in binding order. Native stage-binding rows admit these PS
ids, and the row cap rose from 117 to 137.

The `--transparent-owners-only` lane passes 44 checks with zero validation
errors:

- stock glass resolves to the material ColorAdd (0.25, 0.5, 0.75);
- an owner that swizzles its OIT color gives (0.75, 0.5, 0.25);
- scaling by a catalog SRV (t7) and the b6 render size gives the same at
  0.25, and doubles when the catalog value doubles;
- the pair executes in the current frame, and never in frames without glass;
- a failed compile keeps the previous pair;
- a TransparentForDecals owner that discards every depth fragment removes
  the glass surface, so a lit transparent decal (1.0586) composites to
  exactly zero;
- recording modes agree, and unregistering restores exact stock bits.

### Billboard owners

Stock billboards now have native owner generations, advertised by native owner
flag32 (ids 2560-2624):

- variant k (stock id 20+k) is the VS/PS pair 2560+2*k / 2561+2*k;
- 2624 is the `LDR` vertex shader used by the even additive variants for their
  LDR and PostPP programs.

All 65 modules come from `Transparent/Billboards.hlsl` and activate as one
closure. Owners keep the stock billboard descriptor contract, because no named
stage carries reserved bindings or extras for them. Activation swaps the
rebuilt HDR pair, top pair and LDR pipeline into the stock slots. The PostPP
output program receives the owner modules and layout, and builds at the output
format on first use. HDR format switches swap the owner pairs as they swap
stock.

Billboards have no named stage, and `Overlay/Transparent/*` maps to the
Transparent stage. So on both hosts a raw `Overlay/Transparent/Billboards.hlsl`
is skipped as an unknown named-stage file. Packs reach billboards through
shared headers, such as `Transparent/OIT/Globals.hlsli` (TransparentColorOutput).
Anomaly selects the closure from those header dependencies; injections never
select it.

Framework gap, both hosts: a direct billboard overlay needs a named stage or a
raw-path escape that does not collide with the Transparent stage folder.

The `--billboard-owners-only` lane overlays `Globals.hlsli` and exchanges red
and blue after fog and in the premultiplied path. It passes 58 checks with zero
validation errors:

- Standard OIT (variant 0), AdditiveBottom and AdditiveTop (variant 8) each give
  the stock (0.25, 0.5, 0.75) as (0.75, 0.5, 0.25);
- the LDR and PostPP programs give the stock 137/188/225 as 225/188/137, and
  run the owner LDR vertex shader in the current frame;
- there is no execution in frames without billboards;
- a failed compile keeps the closure;
- the additive pipelines survive an HDR format switch;
- recording modes agree, and unregistering restores exact stock bits.

Still open on this contract:

- voxel owners (not yet native geometry owners);
- GPU OLD instanced/cube execution fixtures;
- probe modes;
- attachment fixtures on native-cell/GPU owners (they share the format helper).

Both aggregate capability flags stay false.
