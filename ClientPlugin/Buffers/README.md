# Buffer catalog

Anomaly publishes named GPU textures so consumers (SE-DLSS, later TAA / motion blur / SSR) **must not** take a compile-time project reference. Resolve the well-known types by name on loaded assemblies.

Velocity stays a typed convenience: bind `ClientPlugin.Velocity.VelocityRegistry.Active` (`IVelocityBuffer`) as today. The catalog `Active("velocity")` aliases the same producer.

## Types (`ClientPlugin.Buffers`)

| Type | Role |
|------|------|
| `ISharedBuffer` | `IsAvailable`, Keen `ISrvBindable` as `object Srv`, `NativeResource`, `Width`/`Height` |
| `BufferCatalog` | `Active(name)` / `Set` (Anomaly internals) / `Publish` / `Unpublish` / `RegisterLifetime` |
| `PublishedBuffer` | Pack-owned `ISharedBuffer` wrapper for `Publish` |

Well-known names:

| Name | Status | Format / size |
|------|--------|----------------|
| `velocity` | Live — aliases `VelocityRegistry.Active` | `RG16F`, internal resolution |
| `linearDepth` | Live — positive view-space Z from resolved complementary depth (`Frame.hlsli` `compute_depth`) | `R32_Float`, full res |
| `hiZ` | Live — 2×2 **min** downsample of `linearDepth` (not Keen `GenerateMips`) | `R32_Float`, half res |
| `historyColor` | Live — previous-frame HDR `LBuffer` copy, published **after** post so this frame’s TAA still sees N−1 | `RGBA16F`, full res |
| `reactiveMask` | Live when an owned pass or IsolatedAdd program sets `TemporalPolicy.Reactive` | `R8_UNorm`, full res; IsolatedAdd auto-stamps dilated luma; 0 = trust history, 1 = reject |
| `objectId` | Live extra GBuffer attachments also resolve through `Active(name)` via `GBufferAttachments.TryGet` | pack-requested |
| `fullscreenIsolated` | Last Anomaly-drawn pack `Fullscreen/` isolated output this frame | `RGBA16F`, slot resolution (`ResolutionI` or viewport), or 1/2 / 1/4 when that program set `scale` |
| `hdrColor` | Live — aliases Keen `LBuffer` (internal HDR) | Same as `LBuffer`; bind without poking `MyGBuffer` |
| `pointLights` | Live after `PreparePointLights` when `DebugOverrides.PointLights` and last-frame visible | 48-byte structured `AnomalyPointLight`; `Width` = count. Wrap Keen `m_pointlightCullHwBuffer`. Dummy 1-element SRV on HDR fullscreen t10 when empty |
| `tileIndices` | Same as `pointLights` | Structured `uint`; wrap Keen `m_tileIndices`. Dummy 1-element SRV on HDR t11 when empty |
| `litMips` | Live at AfterLighting when a live fullscreen program bound `litMips`, `RequestLitMips` was called, or Debug buffer is LitMips | `RGBA16F`, full res, default 5 mips of this-frame `LBuffer` (before atmosphere). Do not `GenerateMips` in pack C# |
| `historyDepth` | Live after two linear frames (unread ping-pong) | `R32_Float` last-frame linear view Z |
| `occupancy` | Live at AfterLighting when `RequestOccupancy` | R8 512×512 atlas of a 64³ camera-relative clipmap (2 m voxels) |
| `pointShadowAtlas` | Live at AfterLighting when `RequestPointShadows(cap > 0)` | RGBA32F light-view mesh (+ optional AABB) depth; default cap 4 / face 128, max 64 / 256 |
| `upscaledColor` | Live after the unique upscale consumer calls `NotifyUpscaleComplete(rc, color)` | Output-res dest (HDR or LDR — whatever the consumer wrote). Cleared each `DrawGameScene` prefix |
| `avgLuminance` | Live after Anomaly captures `MyToneMapping.Run` args (even if a later prefix skips Keen SDR) | Keen eye-adaptation target; Display AfterUpscale t4 |
| `bloom` | Same capture | Keen bloom; Display AfterUpscale t5 |
| `dirt` | Same capture (from the dirt texture name on `Run`) | Display AfterUpscale t6 |
| `pass.<id>` | Named isolated output for a fullscreen program | Same as isolated; not reserved — published by Anomaly for that pack id |

Reserved names (`velocity`, `linearDepth`, `hiZ`, `historyColor`, `reactiveMask`, `fullscreenIsolated`, `hdrColor`, `upscaledColor`, `avgLuminance`, `bloom`, `dirt`, `litMips`, `pointLights`, `tileIndices`, `historyDepth`, `occupancy`, `pointShadowAtlas`) cannot be `Publish`ed by a pack. Same name from two pack ids fails closed. `UnpublishAll(packId)` on dispose.

Linear depth and Hi-Z are filled after GBuffer + lighting (`MyRenderScheduler.Done`) and stay **frozen** for the rest of the frame. Atmosphere, clouds, OIT, and owned AfterAtmosphere draws do **not** update them. `litMips` is filled at AfterLighting (after BeforeFullscreen C#, before fullscreen programs) from this-frame `LBuffer`, then frozen. History is copied at `DrawGameScene` postfix (after Keen post). First frame / resize: `historyColor` stays unavailable until one copy exists. MSAA `LBuffer` is resolved before the blit. Do **not** use `MyCopyToRT.Run` for this copy (other plugins may intercept it).

`BufferCatalog.RequestLitMips(mipLevels)` (or `FullscreenPassRegistry.RequestLitMips`) is the C# ask when you have no fullscreen bind. Json `passes[].binds` `{ "catalog": "litMips", "slot": 7 }` and `RequestSrv(id, "litMips", 7)` also generate while that program is live (`SetEnabled(true)`). `RequestOccupancy` / `RequestPointShadows(maxLights, faceResolution)` fill the occupancy clipmap and capped light-view atlas (default 4 lights, max 64).

`RegisterLifetime(packId, onResolutionChanged, onDeviceEnd)` is how a pack drops its own `OnDeviceReset` Harmony. Anomaly calls those on `CreateScreenResources` / `OnDeviceEnd`.

## Jitter contract

SE-DLSS owns Halton jitter (`Projection.M31` / `M32`). Anomaly reads it into `ClientPlugin.Shaders.FrameTemporal` and republishes an **unjittered** view-projection on the lighting/atmosphere/post extras CB (`AnomalyLightingJitter`, `AnomalyUnjitteredViewProj`, `AnomalyPrevViewProj`, `AnomalyCameraToWorld`, `AnomalyProjScale`). Linearize uses `Projection.M33` / `M43` only, so jitter does not change `linearDepth`. TAA / SSR plugins must not assume Anomaly owns jitter, and must not steal SE-DLSS’s jitter. Call `FrameTemporal.InvalidateHistory()` on a camera cut you own; do not patch the projection.

Velocity is written at `MyRenderScheduler.Done` (**before** transparent). Animated AfterAtmosphere IsolatedAdd with `temporal` `ContributeVelocity` reconstructs camera MVs from isolated.a (view-space hit meters) and republishes `velocity`. `Reactive` instead stamps dilated isolated luma into `reactiveMask` on the transparent deferred `rc` (history reject). Anomaly redirects `MyRender11.RC` to that `rc` during AfterLighting / AfterAtmosphere / AfterTransparent callbacks. Binding `reactiveMask` is the consumer’s job.

AfterUpscale is a **scheduler**, not a color API. `OwnedPassContext.LBuffer` is Keen’s internal HDR lighting buffer. Display tenants (HdrRender-class BT.2390) must sample `upscaledColor` / `ctx.SceneColor`, not raw `LBuffer` at output dispatch size.

Handshake (no compile-time Anomaly reference):

1. Display tenant: ship `Fullscreen/AfterUpscale/*.hlsl` with `temporal: ["InColor","Display"]` and `compose: "Replace"`, or `OwnedPassRegistry.Register("hdr.tonemap", "AfterUpscale", priority, InColor\|Display, draw)`.
2. Unique upscaler: `ClaimUpscale("se-dlss")` at init. Query `HasDisplayTenant` — if true, skip Keen SDR tonemap, evaluate **pre-tonemap** `hdrColor` / `LBuffer` into an output-sized dest, then `NotifyUpscaleComplete(rc, dest)`.
3. Anomaly captures bloom / avg luminance / dirt on `MyToneMapping.Run` (priority above SE-DLSS) and skips Keen SDR when `HasDisplayTenant && !HasUpscaleConsumer`, returning a dest `DrawGameScene` can copy (`DrawGameScene.Tonemapped`, wrapped to `R16G16B16A16_Float` when Keen’s custom texture is 8-bit UNORM). AfterUpscale grades `LBuffer` into that dest at **internal** `ResolutionI` (direct Replace / optional compute when dest ≠ t0). If a unique upscaler skips `Run` without assigning `__result`, Anomaly’s postfix adopts the notified dest (the AfterUpscale image) so `DrawGameScene` does not NRE on `.Linear` / `.SRgb`. After DLSS, dest aliases t0 so Anomaly grades into UAV scratch then copies — `__result` stays the graded UAV. Display HLSL samples t0 scene (`upscaledColor` after notify, else `LBuffer`) plus t4–t6. Keep swapchain / UI composite off AfterUpscale.
4. Only one caller of `NotifyUpscaleComplete` per frame. Display-without-upscale marks notified after grading the dest. If nobody notifies, AfterUpscale falls back at native res with `LBuffer` (no `upscaledColor`).

Two upscale claimers fail closed. Anomaly does not present and does not skip Keen unless a consumer yields.

## Discovery (C# sketch)

```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    var catalog = assembly.GetType("ClientPlugin.Buffers.BufferCatalog");
    if (catalog == null)
        continue;
    var active = catalog.GetMethod("Active")?.Invoke(null, new object[] { "linearDepth" });
    // active is ISharedBuffer; check IsAvailable, then bind Srv / NativeResource
    break;
}
```

SE-DLSS should keep using `VelocityRegistry` / `IVelocityBuffer` (convention flags live there). A second consumer that only needs a texture can bind `linearDepth` / `hiZ` / `historyColor` the same way.

See also [Velocity/README.md](../Velocity/README.md).
