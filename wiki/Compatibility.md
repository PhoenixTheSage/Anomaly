# Compatibility

Anomaly is the shared renderer-extension layer. Renderer plugins integrate through its published buffers, shader packs, attachment allocator, bind registry, and owned-pass scheduler. They must not install a parallel shader compiler interception route or independently claim GBuffer `SV_Target3`.

## Known status

- **SE-DLSS:** integrated consumer of `IVelocityBuffer`. Call `ClaimUpscale` then `NotifyUpscaleComplete(rc, dest)`. When `HasDisplayTenant`, evaluate pre-tonemap `hdrColor`.
- **HdrRender:** handshake ready. Register AfterUpscale with `Display`. Read `ctx.SceneColor`. Yield `MyToneMapping.Run` when `HasUpscaleConsumer`. Keep swapchain / UI composite.
- **SSGI / Prism:** incompatible until migrated. Its audited build replaces Keen shader compilation and independently owns Target3 velocity. It must consume Anomaly velocity and move its shader/pass work onto Anomaly's framework contracts.
- **Aurora:** migration required; its implementation has not yet been audited against the contract.

Other renderer plugins that touch the same compiler, MRTs, or pass boundaries are migration candidates rather than targets for plugin-specific Anomaly workarounds.

See the repository's [full compatibility and migration contract](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Compatibility.md), plus [[Velocity-contract]], [[GBuffer-attachments]], [[Pass-begin-binds]], [[Owned-passes]], and [[Composition-rules]].
