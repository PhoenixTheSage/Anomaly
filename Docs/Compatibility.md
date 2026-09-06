# Compatibility

Anomaly is the shared renderer-extension framework. Plugins that need motion vectors, extra GBuffer data, shader injection, or scheduled fullscreen work integrate through Anomaly instead of installing a parallel compiler or render-pipeline interception layer.

## Ownership rules

- Anomaly owns the Keen shader compile interception route.
- Anomaly owns extra GBuffer render-target allocation. `SV_Target0-2` remain Keen, `SV_Target3` is Anomaly velocity, `SV_Target4-6` are allocated attachments, and `SV_Target7` is reserved for diagnostics.
- Plugins do not patch GBuffer draw setup, independently append MRT outputs, or replace the compiler call used by Keen shader managers.
- Consumers discover published buffers by well-known type name. They do not take a compile-time reference to Anomaly or patch instance-history updates.
- Shader packs register with `ClientPlugin.Shaders.ShaderPackRegistry`. They use additive stage injection by default, request attachments through `GBufferAttachments`, request SRVs through `ShaderBindRegistry`, and schedule work through `OwnedPassRegistry` or data-driven `Fullscreen/` programs.
- Exclusive overlays are explicit and fail closed. Taking exclusive GBuffer ownership opts out of Anomaly's velocity stages and is not a compatibility mechanism for a second renderer framework.

## Migration paths

Choose the narrowest Anomaly contract that provides the required data or execution point:

1. Motion-vector or temporal consumer: resolve `ClientPlugin.Velocity.VelocityRegistry.Active` / `IVelocityBuffer`. The published texture is render-resolution `RG16F`, unjittered, Y-down pixel delta.
2. General buffer consumer: resolve `ClientPlugin.Buffers.BufferCatalog.Active(name)` and check `IsAvailable` before binding.
3. Shader modification: ship a Pulsar asset pack and call `ShaderPackRegistry.Register` during `LoadAssets`. Prefer `Inject/<Stage>`; use an exclusive overlay only when replacing that complete stage is intentional.
4. Extra GBuffer output: request a named attachment. Never hard-code or splice an `SV_TargetN` declaration.
5. Extra resource binding: request a catalog SRV for a named stage through `ShaderBindRegistry`.
6. Fullscreen or temporal work: register an Anomaly-owned pass or declare a `Fullscreen/<Slot>` program. Do not patch Keen pass methods or issue an independent competing draw at the same boundary.

The detailed contracts are in [Velocity/README.md](../ClientPlugin/Velocity/README.md), [Buffers/README.md](../ClientPlugin/Buffers/README.md), [ShaderPacks.md](ShaderPacks.md), and [Extensibility.md](Extensibility.md).

## Known migrations

| Plugin | Status | Required change |
|---|---|---|
| SE-DLSS | Integrated consumer | Continue consuming `IVelocityBuffer`; do not duplicate Anomaly sources. |
| SSGI / Prism | Confirmed incompatible until migrated | Remove its replacement shader compiler and independent Target3 velocity ownership. Consume Anomaly velocity and express its shader/pass work through Anomaly's pack, attachment, bind, and owned-pass APIs. |
| Aurora | Migration required; implementation not yet audited | Convert its renderer hooks to the appropriate Anomaly pack, buffer, attachment, bind, and owned-pass contracts before compatibility is claimed. |

Other renderer plugins that intercept the same compiler, GBuffer targets, or render-pass boundaries should be treated as migration candidates until they use these contracts. This page records integration status; Anomaly does not carry plugin-specific runtime workarounds.

## SSGI evidence

The September 6, 2026 motion-vector audit found that the installed SSGI/Prism build replaced both new- and old-pipeline shader compilation, used a three-argument `__vertex_shader` wrapper, emitted its own velocity to `SV_Target3`, and owned a separate RGBA16F velocity texture. The game log reported twelve replacement failures at SSGI `Pipeline/vs.hlsl` with X3013. Fixing only the wrapper arity would leave Target3 and render-state ownership in conflict, so it is not a valid compatibility fix.

This evidence explains that test environment; it does not make SSGI behavior part of Anomaly's runtime contract.
