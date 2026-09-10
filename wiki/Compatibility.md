# Compatibility

Anomaly is the shared renderer-extension layer. Renderer plugins integrate through its published buffers, shader packs, attachment allocator, bind registry, and owned-pass scheduler. They must not install a parallel shader compiler interception route or independently claim GBuffer `SV_Target3`.

## Known status

- **SE-DLSS:** integrated consumer of `IVelocityBuffer`. Call `ClaimUpscale` then `NotifyUpscaleComplete(rc, dest)`. When `HasDisplayTenant`, evaluate pre-tonemap `hdrColor`.
- **HdrRender:** implemented Display pack. AfterUpscale `Replace` + `Display` (`hdr.tonemap`). Samples `upscaledColor` / t0 and captured bloom/avgLum/dirt at t4–t6. Anomaly skips Keen SDR when Display is live and no upscaler claimed. Keep swapchain / UI composite. Rich HUD: **Anomaly Shaders → HDR → Settings**.
- **SSGI / Prism:** migrated pack. Consumes Anomaly `velocity` (RG16F), `linearDepth`, and catalog `litMips`. Trace is `Fullscreen/AfterLighting` `PublishOnly`; C# BeforeFullscreen writes uniforms / `SetEnabled`; AfterFullscreen runs SVGF into `LBuffer`. No compiler intercept, no Target3, no pack ViewGuard. Rich HUD: **Anomaly Shaders → SSGI → Settings**.
- **Aurora:** integrated pack. AfterAtmosphere IsolatedAdd (`aurora.borealis.curtain`), catalog `aurora.noise` / `aurora.ramp`, Rich HUD **Anomaly Shaders → Aurora Borealis → Settings**.

Other renderer plugins that touch the same compiler, MRTs, or pass boundaries are migration candidates rather than targets for plugin-specific Anomaly workarounds.

Terminal pages register on `ClientPlugin.RichHud.TerminalConfigRegistry` and appear under **Anomaly Shaders** beside the **Anomaly** folder. Packs do not vendor Rich HUD or list Master as a Pulsar dependency. See [[Terminal-config]].

See the repository's [full compatibility and migration contract](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Compatibility.md), plus [[Velocity-contract]], [[GBuffer-attachments]], [[Pass-begin-binds]], [[Owned-passes]], and [[Composition-rules]].
