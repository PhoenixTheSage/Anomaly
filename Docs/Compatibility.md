# Compatibility

Anomaly is the shared renderer-extension framework. Plugins that need motion vectors, extra GBuffer data, shader injection, or scheduled fullscreen work integrate through Anomaly instead of installing a parallel compiler or render-pipeline interception layer.

## Ownership rules

- Anomaly owns the Keen shader compile interception route.
- Anomaly owns extra GBuffer render-target allocation. `SV_Target0-2` remain Keen, `SV_Target3` is Anomaly velocity, `SV_Target4-6` are allocated attachments, and `SV_Target7` is reserved for diagnostics.
- Plugins do not patch GBuffer draw setup, independently append MRT outputs, or replace the compiler call used by Keen shader managers.
- Consumers discover published buffers by well-known type name. They do not take a compile-time reference to Anomaly or patch instance-history updates.
- Shader packs register with `ClientPlugin.Shaders.ShaderPackRegistry`. They use additive stage injection by default, request attachments through `GBufferAttachments`, request SRVs through `ShaderBindRegistry`, and schedule work through `OwnedPassRegistry` or data-driven `Fullscreen/` programs.
- Terminal pages register with `ClientPlugin.RichHud.TerminalConfigRegistry.RequestPage(title)` and appear under **Anomaly Shaders**. Corner status uses `HudOverlayRegistry.Register`. Packs do not vendor Rich HUD or list Master as a Pulsar dependency. Pulsar MyGui remains the fallback when Master is absent.
- Exclusive overlays are explicit and fail closed. Taking exclusive GBuffer ownership opts out of Anomaly's velocity stages and is not a compatibility mechanism for a second renderer framework.

## Migration paths

Choose the narrowest Anomaly contract that provides the required data or execution point:

1. Motion-vector or temporal consumer: resolve `ClientPlugin.Velocity.VelocityRegistry.Active` / `IVelocityBuffer`. The published texture is render-resolution `RG16F`, unjittered, Y-down pixel delta.
2. General buffer consumer: resolve `ClientPlugin.Buffers.BufferCatalog.Active(name)` and check `IsAvailable` before binding.
3. Shader modification: ship a Pulsar asset pack and call `ShaderPackRegistry.Register` during `LoadAssets`. Prefer `Inject/<Stage>`; use an exclusive overlay only when replacing that complete stage is intentional.
4. Extra GBuffer output: request a named attachment. Never hard-code or splice an `SV_TargetN` declaration.
5. Extra resource binding: request a catalog SRV for a named stage through `ShaderBindRegistry`.
6. Fullscreen or temporal work: register an Anomaly-owned pass or declare a `Fullscreen/<Slot>` program. Do not patch Keen pass methods or issue an independent competing draw at the same boundary.
7. Player-facing options: `TerminalConfigRegistry.RequestPage` under **Anomaly Shaders** (sibling of the **Anomaly** folder). Corner status: `HudOverlayRegistry.Register`. Do not open a second Rich HUD root. Reserved titles `Anomaly`, `Settings`, and `Velocity Debug` fail closed.

The detailed contracts are in [Velocity/README.md](../ClientPlugin/Velocity/README.md), [Buffers/README.md](../ClientPlugin/Buffers/README.md), [ShaderPacks.md](ShaderPacks.md), and [Extensibility.md](Extensibility.md).

## Known migrations

| Plugin | Status | Required change |
|---|---|---|
| SE-DLSS | Integrated consumer | Continue consuming `IVelocityBuffer`. Call `ClaimUpscale` then `NotifyUpscaleComplete(rc, dest)` so AfterUpscale sees the dest. When `HasDisplayTenant`, evaluate pre-tonemap `hdrColor` and skip Keen SDR. A skip of `MyToneMapping.Run` must still return a dest — `DrawGameScene` NREs on `.Linear` / `.SRgb` otherwise. Anomaly adopts the notified dest (or borrows the Display wrap) when the skipper omits `__result`. |
| HdrRender | Implemented Display pack | AfterUpscale `Replace` + `Display` (`hdr.tonemap`). Samples `upscaledColor` / t0 and captured bloom/avgLum/dirt at t4–t6. Anomaly skips Keen SDR when Display is live and no upscaler claimed, and still supplies a dest when an upscaler skipped `Run` without one. Keep swapchain / UI composite. |
| SSGI / Prism | Migrated pack | Consumes Anomaly `velocity` (RG16F), `linearDepth`, `hiZ`, and catalog `litMips`. Trace is `Fullscreen/AfterLighting` `PublishOnly` at `scale` 1/2 (Low 1/4) via `SetScale`; C# BeforeFullscreen writes uniforms / `SetEnabled` / `SetScale`; AfterFullscreen runs SVGF at pass size and upsamples into `LBuffer`. No compiler intercept, no Target3, no pack ViewGuard. Rich HUD: **Anomaly Shaders → SSGI → Settings**. |
| Aurora | Integrated pack | AfterAtmosphere `Fullscreen/` IsolatedAdd (`aurora.borealis.curtain`). Catalog `aurora.noise` / `aurora.ramp`. Rich HUD **Anomaly Shaders → Aurora Borealis → Settings**. |
| Volumetric Clouds | Integrated pack | AfterAtmosphere PublishOnly `volumeSunShadow` + IsolatedSub dest + IsolatedMix (`volumetric.clouds.shadow` / `.occlude` / `.volume`). t7–t9 `Texture3D` via `ANOMALY_PACK_SRVn_TYPE` on the mix pass. Catalog `clouds.shape` / `clouds.detail` / `clouds.weather`. Harmony skip of Keen `MyCloudRenderer.Render` when Enabled+Replace. IsolatedMix RGB is lit from extras `AnomalySunColor` / `AnomalySunTransmittance` / `AnomalyVolumeNight` (AJ × 0.05 at night; monotonic squared-limb twilight). Dest shadows use `AnomalyVolumeSunShadow`. `passes[].scale` 0.5 is pixel cost. Do not bind Keen CSM. Rich HUD **Anomaly Shaders → Volumetric Clouds → Settings**. |
| Screen Space Shadows | Integrated pack | AfterLighting IsolatedSub (`screenspace.shadows.contact`): pack writes photometric `AnomalyIsolatedSubEnergy` (0–1 dest fraction); Anomaly blends `LBuffer * (1-src)` onto dest. `sum L_i*(1-vis_i)`, ~1.4× photometric march, `AnomalyIgnWorld` on unjittered view, `AnomalyViewToDepthUv`. IsolatedSub mins `pointShadowAtlas` (Slice AL MeshDepth; AABB optional). `temporal` InColor+Reactive stamps `reactiveMask` from `.a`. Debug overlay is Replace (`screenspace.shadows.debug`). A live debug Replace skips IsolatedSub that frame only. Magenta = atlas hit, contact miss. Status: **Anomaly Shaders → Screen Space Shadows → Status**. Catalog `pointLights` / `tileIndices` plus `historyDepth` / `occupancy` / `pointShadowAtlas`. Occupancy / temporal / light-views default off. Rich HUD **Anomaly Shaders → Screen Space Shadows → Settings**. |

Other renderer plugins that intercept the same compiler, GBuffer targets, or render-pass boundaries should be treated as migration candidates until they use these contracts. This page records integration status; Anomaly does not carry plugin-specific runtime workarounds.

## SSGI evidence (historical)

The September 6, 2026 motion-vector audit found that the then-installed SSGI/Prism build replaced both new- and old-pipeline shader compilation, used a three-argument `__vertex_shader` wrapper, emitted its own velocity to `SV_Target3`, and owned a separate RGBA16F velocity texture. The game log reported twelve replacement failures at SSGI `Pipeline/vs.hlsl` with X3013. That build is retired. The current pack consumes Anomaly velocity and linearDepth and does not intercept the compiler or Target3.

This evidence explains that test environment; it does not make SSGI behavior part of Anomaly's runtime contract.
