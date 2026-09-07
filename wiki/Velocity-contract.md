# Velocity contract

## Direction revision (September 6, 2026)

The canonical output is now current-to-previous, in unjittered internal-resolution pixels, Y down: `previousPixel = currentPixel + motion`. `VelocityConvention.CurrentToPrevious = 8` makes combined flags 15. Legacy flags 7 mean the earlier current-minus-previous direction. Both geometry and camera fallback use the new direction. Direct NGX uses scale (+1,+1) for flags 15; a legacy producer needs (-1,-1). Consumers must check direction to avoid double negation.

Debug colors encode the backward vector, not the object's travel direction: rightward travel now lowers red, downward travel lowers green. Speed/blue and persistence magnitudes are unchanged. Probe constants and their diagnostic colors are unchanged; they are wire tests, not physical motion. Numerical and temporal correctness remain under audit.

Typed convenience for motion vectors. Catalog `Active("velocity")` aliases the same producer as `VelocityRegistry.Active`. Prefer the registry if you need convention flags.

| Field | Meaning |
|-------|---------|
| `Srv` / `NativeResource` | Keen SRV or `ID3D11Resource*` |
| `Width` / `Height` | Internal (DRS) size |
| `Convention` | Unjittered, PixelSpace, MatchesRenderResolution |
| `HistoryValid` | False on first frame, camera cut (~80 m), or resize |

## Units

RG16F pixel delta at internal resolution. Y-down D3D (top of the RT is v = 0). Debug overlay: complementary-depth 0 (sky / uncleared GBuffer; Keen `IsDepthForeground` is `hw > 0`) is dark grey so still meshes silhouette; geometry rest is mid-gray (R/G signed delta around 0.5, B = 0.5 + speed); magenta is invalid history on geometry. The catalog texture still camera-fills sky; the overlay ignores those MVs.

## Settings (in-game proof)

Velocity source: GBuffer (object motion on Keen GBuffer pixels, camera fill on complementary-depth 0 / sky) or CameraOnly (fullscreen depth reprojection). Transparent, glass / holo / shield, foliage, and deferred decals still do not write Target3; they receive camera-from-depth today. That leftover coverage is near-future Slice M, not a consumer bug. The GPU never sees ActorIDs. CPU history is keyed by `MyInstance.ActorID` / `IMyActor.ID`. Stage 2 packs those worlds into t15 in instance-buffer order; the VS reads `t15[SV_InstanceID + InstanceBase]` (`InstanceBase` is the group's `OffsetInInstanceBuffer`). CPU packing happens on the prepare worker, but the t15 GPU upload is recorded on the Stage 2 GBuffer's own deferred context at pass begin—never through the global immediate context from a parallel worker. Every render context owns a ring of exact-layout velocity CBs; each entry is mapped only once per frame. The native b6/b7 bind uses whole-buffer binding on dedicated 256-byte resources immediately before each draw, while HLSL-visible layouts remain 224 bytes for b6 and 16 bytes for b7. Player ships are old-pipeline cube instancing: VS b6 `HasPrevWorld` is CPU `currToPrev` from last **Main**-view object-CB rows. The separate **Velocity Debug** page owns visualization, persistence, probes, checkpoints, and **Velocity Status**. `GBufferVelocityRaw` bypasses camera fallback. Debug scale maps motion to color; 1 is most sensitive. Probe transitions are immediate and do not rebuild shaders. Non-Live checkpoints copy only their selected boundary and never affect the published buffer. Pipeline Audit allocates and binds its RGBA16F Target7 only while selected; returning to Live/Off releases checkpoint and visualization resources. Full bind counters remain in the debug log instead of the status dialog.

The current native draw-boundary contract supersedes the earlier D3D11.1 ranged-bind experiment: dedicated b6 and b7 buffers are each physically 256 bytes and are bound as whole buffers immediately before the draw. This resets inherited first/count range state while preserving the 224-byte and 16-byte logical HLSL layouts. During the developer-only `MrtWrite` probe, native state queries verify VS, PS, b6, b7, and Target3; `VelocityPipelineAudit` additionally checks reserved Target7 and displays six panels. These expensive checks and targets are opt-in. Velocity Status gives a screenshot-sized summary while the debug log retains raw counters.

Shader-object proof is generation-aware and attached to the exact `MyPixelShaders.Init` / `MyVertexShaders.Init` call that creates each native object. Anomaly keeps a per-stage, per-thread Init stack while Keen resolves that object. The authoritative interception point is Keen's deepest eleven-argument `MyShaderCompiler.Compile` overload: it is the last common boundary before preprocessing, hashing, cache lookup, or FXC. Anomaly inserts the GBuffer macros there before the cache identity is computed, then records the exact byte array returned by either the cache or fresh-compile branch. This covers manager wrappers that the CLR inlines or lowers without a stable IL call site. Manager transpilers, the five-argument wrapper, the cache hook, and the vertex out value remain independent diagnostics rather than required proof. Keen retains ownership of cache synchronization; Anomaly does not force fresh compiles. No global "last bytecode" slot is used, so nested initialization and parallel shader creation cannot cross-associate evidence. A healthy resident generation reports `Compile endpoint hook=yes`, endpoint V/P counts equal to the velocity Init counts, `full` equal to those endpoint counts, `orphan=0/0`, `badV=- badP=-`, and verified native-object counts equal to the Init counts.

Correction from the September 6 test (`5d0e3ae3`): endpoint calls need not run under Init. The test reports 13/13 unowned calls, only 10/22 verified objects per stage, and 0/1183 verified drawn pairs. Therefore the count-equality and zero-orphan expectations above are retired. Unowned results are now reflected independently and bounded call stacks are logged in Release; they are never attached to another object's state. The latest candidate repairs unverified GBuffer objects at Init return through the common compiler endpoint, without cache invalidation. Replacement requires complete executable flow; failures preserve the original. The vertex out-bytecode and new native object use the identical replacement array. No live draw objects or shader-cache files are modified. Historical compile warnings remain distinct from current resident/drawn-object proof.

The developer window is intentionally a compact proof summary rather than a raw counter dump. `[PASS]` / `[WARN]` groups cover compile/refresh, include ownership, exact object association, executable DXBC flow, CPU history, MRT/constant-buffer state, and draw-pair resolution. The Object repair row reports attempts/replacements/failures. The draw-contract summary is not motion proof: first require MrtWrite pink on covered geometry, then probe Off and test camera movement, object-only movement, and rest. Long route identities and stacks go to SpaceEngineers.log; screenshot status shows a compact count instead.

> **Note — NVIDIA consumer flags.** For DLSS-class APIs: MVJittered off, MVLowRes on. Those are consumer flags, not Anomaly create flags.

```csharp
var registry = assembly.GetType("ClientPlugin.Velocity.VelocityRegistry");
var active = registry?.GetProperty("Active")?.GetValue(null);
// IVelocityBuffer — if null or !IsAvailable, keep camera-only fallback
```

→ [[Owned-passes|When the pass runs]]
