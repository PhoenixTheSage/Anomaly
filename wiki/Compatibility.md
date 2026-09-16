# Compatibility

Anomaly is the shared renderer-extension layer. Renderer plugins integrate through its published buffers, shader packs, attachment allocator, bind registry, and owned-pass scheduler. They must not install a parallel shader compiler interception route or independently claim GBuffer `SV_Target3`.

## Known status

- **SE-DLSS:** integrated consumer of `IVelocityBuffer`. Call `ClaimUpscale` then `NotifyUpscaleComplete(rc, dest)`. When `HasDisplayTenant`, evaluate pre-tonemap `hdrColor`.
- **HdrRender:** implemented Display pack. AfterUpscale `Replace` + `Display` (`hdr.tonemap`). Direct dest / optional compute when dest ≠ t0; scratch+copy when DLSS dest aliases t0. Samples `upscaledColor` / t0 and captured bloom/avgLum/dirt at t4–t6. Anomaly skips Keen SDR when Display is live and no upscaler claimed. Keep swapchain / UI composite. Rich HUD: **Anomaly Shaders → HDR → Settings** (Master optional).
- **SSGI / Prism:** migrated pack. Consumes Anomaly `velocity` (RG16F), `linearDepth`, `hiZ`, and catalog `litMips`. Trace is `Fullscreen/AfterLighting` `PublishOnly` at `scale` 1/2 (Low 1/4) via `SetScale`; C# BeforeFullscreen writes uniforms / `SetEnabled` / `SetScale`; AfterFullscreen runs SVGF at pass size and upsamples into `LBuffer`. No compiler intercept, no Target3, no pack ViewGuard. Rich HUD: **Anomaly Shaders → SSGI → Settings**.
- **Aurora:** integrated pack. AfterAtmosphere IsolatedAdd (`aurora.borealis.curtain`), catalog `aurora.noise` / `aurora.ramp`, Rich HUD **Anomaly Shaders → Aurora Borealis → Settings**.
- **Volumetric Clouds:** AfterAtmosphere IsolatedMix (`volumetric.clouds.volume`). Pack extras t7–t9 are `Texture3D` via `ANOMALY_PACK_SRV0_TYPE` / `SRV1_TYPE` / `SRV2_TYPE` before `#include <AnomalyFullscreen.hlsli>`. Catalog `clouds.shape` / `clouds.detail` / `clouds.weather`. Harmony skip of Keen `MyCloudRenderer.Render` when Enabled+Replace. IsolatedMix RGB is lit from extras `AnomalySunColor` / `AnomalyVolumeAmbient()`. `passes[].scale` 0.5 is pixel cost. Rich HUD **Anomaly Shaders → Volumetric Clouds → Settings**.
- **Screen Space Shadows:** AfterLighting IsolatedSub (`screenspace.shadows.contact`) writes `AnomalyIsolatedSub` (0–1 dest fraction); Anomaly blends `LBuffer * (1-src)` onto dest. Debug overlay is Replace (`screenspace.shadows.debug`). A live debug Replace skips IsolatedSub that frame only. Status: **Anomaly Shaders → Screen Space Shadows → Status**. Catalog `pointLights` / `tileIndices` plus `historyDepth` / `occupancy` / `pointShadowAtlas`. Occupancy / temporal / light-views default off. Darkens only point-light energy. Rich HUD **Anomaly Shaders → Screen Space Shadows → Settings**.

Other renderer plugins that touch the same compiler, MRTs, or pass boundaries are migration candidates rather than targets for plugin-specific Anomaly workarounds.

Terminal pages register on `ClientPlugin.RichHud.TerminalConfigRegistry` and appear under **Anomaly Shaders** beside the **Anomaly** folder. Corner status uses `HudOverlayRegistry`. Packs do not vendor Rich HUD or list Master as a Pulsar dependency. See [[Terminal-config]].

See the repository's [full compatibility and migration contract](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Compatibility.md), plus [[Velocity-contract]], [[GBuffer-attachments]], [[Pass-begin-binds]], [[Owned-passes]], and [[Composition-rules]].
