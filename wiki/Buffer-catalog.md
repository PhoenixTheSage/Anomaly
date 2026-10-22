# Buffer catalog

Vulkan CPU-backed PublishedBuffer uploads retain cloned RGBA textures, volume mip chains and structured floats across device replacement. Device-end callbacks run before reupload; failed callbacks block guest submission for retry. Retained GuestRendererImage wrappers expire with their native resource epoch. Context teardown/recreation GPU coverage includes exact typed reads of restored payloads and fresh volume/occupancy/spectator-shadow output; loaded-world device-loss and visual acceptance remain separate.

Resolve `ClientPlugin.Buffers.BufferCatalog` by type name. `Active(name)` never returns null — check `IsAvailable`. `Srv` is Keen `ISrvBindable` boxed as object.

| Name | When it exists | Format |
|------|----------------|--------|
| `velocity` | After the depth-equal object pass and the camera fill | RG16F, internal resolution. Non-zero object motion is kept. Sky, skinned characters, voxels, foliage, and decals are camera fill. DX11 still writes object motion on GBuffer `SV_Target3`. |
| `linearDepth` | After scheduler Done, only if a pack is live or Debug buffer is Linear depth / Hi-Z | R32_Float, full res, positive view Z |
| `hiZ` | Right after linearDepth, only if a pack is live or Debug buffer is Hi-Z | R32_Float, half res, 2×2 min (not GenerateMips) |
| `historyColor` | After DrawGameScene postfix, only if a pack is live or Debug buffer is History color | RGBA16F HDR LBuffer copy; unpublished until one copy |
| `reactiveMask` | When an owned pass or IsolatedAdd program sets `TemporalPolicy.Reactive` | R8, full res; IsolatedAdd auto-stamps dilated luma **on the slot `rc`** (transparent deferred worker for AfterAtmosphere). Anomaly redirects `MyRender11.RC` during those callbacks. White = do not trust history |
| `objectId` (or any attachment name) | If a pack requested it | Pack format. DX11: `GBufferAttachments.TryGet`, `SV_Target4`–`6`. Vulkan: catalog `Active(name)`, sentinel `0xFFFF000000000009` plus the slot (`SV_Target4` is slot 0). |
| `fullscreenIsolated` | After a `Fullscreen/` program runs | Last isolated RT (slot res, or 1/2 / 1/4 when that program set `scale`); reserved |
| `hdrColor` | When GBuffer exists | Aliases Keen `LBuffer` (internal HDR) |
| `pointLights` | After `PreparePointLights` when point lights are on and last-frame visible | Structured 48-byte `AnomalyPointLight`; `Width` = element count, `Height` = 1. Wrap, no copy. Dummy 1-element SRV on HDR t10 when empty |
| `tileIndices` | Same as `pointLights` | Structured `uint`; 16×16 tiles then per-tile light indices. Dummy 1-element SRV on HDR t11 when empty |
| `litMips` | AfterLighting, only if a live fullscreen program bound `litMips`, `RequestLitMips` was called, or Debug buffer is LitMips | RGBA16F, full res, default 5 mips of this-frame `LBuffer` (before atmosphere). Reserved |
| `historyDepth` | After scheduler Done, once two linear frames exist | R32_Float unread linear ping-pong (last frame’s view Z). Reserved |
| `occupancy` | AfterLighting after BeforeFullscreen, if `RequestOccupancy`; Vulkan requires current-frame depth and native packet features | R8 on DX11, R32F on Vulkan; 512×512 atlas of a 64³ camera-relative clipmap (2 m voxels). Production depth splat + bounded actor AABB stamps. Reserved |
| `pointShadowAtlas` | DX11 and Vulkan AfterLighting, if `RequestPointShadows(cap > 0)` | RGBA32F skinned light-view mesh plus optional AABB depth. Width = faceRes×6, height = 1 + faceRes×allocated lights. Row 0 retains viewPos.xyz/range and caster validity. Default cap 4 / face 128, max 64 / 1024, bounded by dimensions and roughly 96 MiB. Reserved |
| `playerDepth` | DX11 and Vulkan with the requested character atlas | R32F render-sized Euclidean depth of the current character LOD, far-cleared when absent. Reserved |
| `upscaledColor` | After `NotifyUpscaleComplete(rc, color)` | Unique upscale dest at output res. Cleared next frame. Reserved |
| `pass.<id>` | Same draw | That program’s isolated output (scaled when `passes[].scale` is 0.5 or 0.25) |
| `volumeSunShadow` | After a PublishOnly program with `output: volumeSunShadow` | RGBA remaining sun (`.r` = 1 none). `.a < 0.5` is missing. Not reserved. Helper `AnomalyVolumeSunShadow` |

```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    var catalog = assembly.GetType("ClientPlugin.Buffers.BufferCatalog");
    if (catalog == null) continue;
    var active = catalog.GetMethod("Active")?.Invoke(null, new object[] { "linearDepth" });
    // ISharedBuffer: IsAvailable, Srv, NativeResource, Width, Height
    break;
}
```

`RequestLitMips` / `RequestOccupancy` / `RequestPointShadows(maxLights, faceResolution)` / `RequestWorldBoxes` are C# asks on `BufferCatalog` (also forwarded from `FullscreenPassRegistry`). Cube cap default **4**, max **64**; face default **128**, max **1024**, bounded by dimensions and roughly 96 MiB. `historyDepth` needs no request — it is the unread linear ping-pong after the second frame.

## Publish (pack-owned names)

`BufferCatalog.Publish(packId, name, buffer)` / `Unpublish` / `UnpublishAll`. Reserved names fail closed. Two pack ids on the same name fail closed. Use `PublishedBuffer` as the `ISharedBuffer`. `PublishedBuffer.Publish` is **2D only** (unique name — do not `GetMethod("Publish")` if Anomaly later adds overloads; bind the four-arg signature). Texture3D is `Publish3D`; UAV bake is `PublishBake`; node pools are `PublishStructured`. `RegisterLifetime` is DRS / device-end — drop your own `OnDeviceReset` Harmony.

> **Warning — Jitter.** SE-DLSS owns Halton jitter (Projection M31/M32). Anomaly reads it into `FrameTemporal` and republishes an unjittered VP on the extras CB. Linearize uses M33/M43 only. Do not steal jitter.

→ [[Velocity-contract|Velocity-specific flags]] · [[Frame-graph|When buffers freeze]]

## Vulkan render-target requests (2026-10-21 candidate)

`CatalogImages.EnsureColorTarget(packId, name, width, height, format)` now retains
the requested DXGI format (2/10/28/34/41/54), plus legacy0 (RGBA8) and1 (FP16), defers device work until the renderer
callback and reuses its image ID on resize. An identical ensure preserves image
contents. `ClearColorTarget` queues a floating-point clear, including FP16 alpha
-1 and HDR values. Names have one target owner; a conflicting owner is refused.
Unpublish or unregister queues native retirement. Prepare target allocation and
retirement before queuing frame passes. New operations require SE-Vulkan native
feature bit13; old hosts return an actionable update refusal.


## Core publication ownership and retirement

Core catalog publication uses the DX11 case-insensitive ownership rules. Public
Publish trims pack/name identifiers and refuses empty names, null buffers,
reserved products and a second owner. Requested Vulkan targets and ordinary
publications share the same ownership lock. Wrong-owner unpublish preserves the
existing entry. Repeating Publish with the same object retains its allocation.
IsReservedName is exposed through the core reflection facade as on DX11.

Replacing or unpublishing an owned PublishedBuffer queues its native image for
SE-Vulkan retirement after prior submissions. UnpublishAll and public pack
unregister remove both uploaded publications and requested targets. CPU payloads
remain cloned and reusable after unpublish; an explicit republish recreates the
upload. Clear also discards the retained payload. Target and retirement commands
flush on the renderer callback; publications accepted during renderer selection
replay after selection. Borrowed DX11 resources retain their existing ownership semantics.

The focused actual-core lifecycle fixture passes102 checks: malformed and reserved
publications, competing owners, pending selection, both target/upload transitions,
legacy FP16 alpha -1, structured/mip-volume cleanup, a pipelined read surviving
replacement and eight unregister cycles preserving another pack. The combined
actual-core gate passes766 checks with zero core/synchronization errors. Broader memory-plateau/performance qualification remains
stage8 work.

The raw SRV methods Publish/Publish3D/PublishBake/PublishStructured described above
belong to the DX11 device path. Portable CPU uploads use PublishRgba8,
PublishVolumeRgba8, PublishVolumeMips and PublishStructuredFloats. DX11 target
format/disposal integration and Vulkan HDR/jitter remain separate parity work;
the existing DLSS jitter ownership note describes the DX11 path.


## Reserved color-history timing and size

Vulkan freezes the valid previous historyColor reference once per frame/device
epoch before queuing consumers. Native named-stage bindings and fullscreen/
portable consumers use the same reference and exact prior-production age. Missing,
disabled, resized, recreated or camera-invalid history is unavailable until a
valid producer frame has been recorded again. Capture still executes the named
owned HistoryCopy shader, with command values copied before callbacks can change
registrations.

The capture runs after every AfterUpscale fullscreen pass, Chain presentation and
AfterFullscreen portable callback. Its target uses the HDR render dimensions,
matching DX11 OwnedBuffersPass.EnsureHistoryTarget and MyRender11.ResolutionI.
Ordinary AfterUpscale programs keep output dimensions and their existing DX11
fullscreen scaling rules, independently of renderer DRS rounding.

The focused actual-core gate passes125 checks: early/late/portable reads agree on
exact preceding values, while tail capture writes current HDR pixels. It covers
first use, disable/re-enable, odd resize, 50/75/100% DRS, both recording modes,
current capture dimensions and nonzero recovery, with zero core/synchronization
errors. An independent packet regression reproduces the former sizing error and
checks that ordinary AfterUpscale floor/Replace scaling stays unchanged. The final
combined actual-core gate passes891 checks with zero core/synchronization errors.


## Shared catalog formats, facade and DX11 target lifecycle

Both adapters compile the same device-free names/reservations and color-format
mapping. Core exposes all20 DX11 catalog constants and matching PublishedBuffer
properties, constructors and method signatures. Raw SRV/UAV publication remains
DX11-only. Portable CPU buffers expose IsAvailable, IsStructured, Stride and
metadata; structured Width is element count. Vulkan NativeResource stays zero,
Srv/Uav stay null, and SE-Vulkan retains ownership of actual GPU execution.

EnsureColorTarget supports canonical DXGI2/10/28/34/41/54 and legacy0 (RGBA8)/1
(FP16). DX11 now honors the actual requested format. Shared ownership rejects
reserved/competing names before allocation, and failed allocation preserves the
previous upload/target and its requested size. Target descriptions and clears
accepted before renderer/device availability replay after selection. Acceptance
is registration; IsAvailable describes resource availability, not completed GPU
execution. Match Anomaly.dll and Anomaly.Dx11.dll; older satellites are refused
with an update message before a new format request can be misinterpreted.

DX11 target allocation/clear and retirement run on the renderer thread using
Keen's RwTextures manager and DisposeTex, with a coalesced renderer update queue
and frame-begin flush. Unpublish, replacement and direct legacy-satellite calls
cancel core replay and return owned targets. Borrowed resources keep caller
ownership. Retained DX11 target wrappers refresh their resource pointer after
recreation and become unavailable on unregister; native target snapshots also
invalidate when replaced or resized, so query catalog images for each frame.

The actual-core/linked-production DX11 WARP gate passes134 checks on each runtime,
including typed pixel reads, pending/canceled work, rollback, deferred GPU reads,
actual device replacement and16 unregister cycles returning pool counts to zero.
The engine pooling adapter is not loaded-game acceptance. The native actual-core
catalog gate passes105 checks with zero validation errors. The final combined
gate passes894 checks with zero core/synchronization errors; broader stage8 memory/performance acceptance remains.


CPU upload mode changes now reset dimension metadata and release inactive CPU
payloads. Invalid RGBA sizes, mip chains and structured strides leave the prior
publication intact; structured strides follow DX11's aligned 2048-byte maximum.
Volume mip upload copies each logical mip extent and excludes trailing padding,
so later mip offsets match DX11 subresources and survive device recreation.
The upload-transition WARP gate passes143 checks on each runtime and the native
catalog focus passes109 checks with zero validation errors. The combined898-check
GPU gate passes with zero core/synchronization errors; no native ABI/library change.


### Classic fullscreen null SRVs — bit23

Ordinary fullscreen inputs now use genuine null descriptors for absent or
incompatible catalog images/buffers. Texture2D, Texture3D, Texture2DArray and
typed-buffer dimensions/reads are zero; StructuredBuffer dimensions report zero
elements and the shader's declared stride. Sampling a null image returns zero.
Compatible 2D/3D/structured resources still bind their actual payloads. Structured
uploads are not typed-buffer views. Extended volume/MRT inputs remain required.

This requires coordinated SE-Vulkan native feature bit23 and device nullDescriptor
support. ABI-1 packet layouts are unchanged. The compiled SPIR-V resource bindings
select the requirement; optimized-out unused declarations do not cause refusal.
Older hosts report capability-missing with an update message before native pipeline
allocation. ShaderPackRegistry.GetCapabilities().ClassicNullResourcesSupported
reports host/device support, independently of current-frame shader production.
No device robustness2 buffer/image access modes are enabled by this change.

The actual-core focused GPU gate passes100 checks with core/synchronization
validation enabled and zero errors, including both recording modes, missing
resources, type changes and unregister. Seven older-bit22-host checks pass against
the previously accepted native library. The shared CPU groups and both managed
builds pass. The final combined998-check GPU gate passes with validation enabled
and zero errors. Native CTest executes16 passes plus the configured reference
skip. Other native owners,
same-draw attachments, stage7 and broad automated stage8 remain open.


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
