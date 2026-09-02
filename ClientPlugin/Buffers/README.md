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
| `reactiveMask` | Live when an owned pass sets `TemporalPolicy.Reactive` | `R8_UNorm`, full res; 0 = trust history, 1 = reject |
| `objectId` | Live extra GBuffer attachments also resolve through `Active(name)` via `GBufferAttachments.TryGet` | pack-requested |
| `fullscreenIsolated` | Last Anomaly-drawn pack `Fullscreen/` isolated output this frame | `RGBA16F`, slot resolution (`ResolutionI` or viewport) |
| `pass.<id>` | Named isolated output for a fullscreen program | Same as isolated; not reserved — published by Anomaly for that pack id |

Reserved names (`velocity`, `linearDepth`, `hiZ`, `historyColor`, `reactiveMask`, `fullscreenIsolated`) cannot be `Publish`ed by a pack. Same name from two pack ids fails closed. `UnpublishAll(packId)` on dispose.

Linear depth and Hi-Z are filled after GBuffer + lighting (`MyRenderScheduler.Done`) and stay **frozen** for the rest of the frame. Atmosphere, clouds, OIT, and owned AfterAtmosphere draws do **not** update them. History is copied at `DrawGameScene` postfix (after Keen post). First frame / resize: `historyColor` stays unavailable until one copy exists. MSAA `LBuffer` is resolved before the blit. Do **not** use `MyCopyToRT.Run` for this copy (other plugins may intercept it).

`RegisterLifetime(packId, onResolutionChanged, onDeviceEnd)` is how a pack drops its own `OnDeviceReset` Harmony. Anomaly calls those on `CreateScreenResources` / `OnDeviceEnd`.

## Jitter contract

SE-DLSS owns Halton jitter (`Projection.M31` / `M32`). Anomaly reads it into `ClientPlugin.Shaders.FrameTemporal` and republishes an **unjittered** view-projection on the lighting/atmosphere/post extras CB (`AnomalyLightingJitter`, `AnomalyUnjitteredViewProj`, `AnomalyPrevViewProj`). Linearize uses `Projection.M33` / `M43` only, so jitter does not change `linearDepth`. TAA / SSR plugins must not assume Anomaly owns jitter, and must not steal SE-DLSS’s jitter. Call `FrameTemporal.InvalidateHistory()` on a camera cut you own; do not patch the projection.

Velocity is written at `MyRenderScheduler.Done` (**before** transparent). Animated AfterAtmosphere emission is invisible to DLSS unless the pass calls `OwnedPassContext.ContributeVelocity` and/or writes `reactiveMask`. SE-DLSS evaluates **LDR after tonemap** and should call `OwnedPassRegistry.NotifyUpscaleComplete()` after evaluate. Binding `reactiveMask` is the consumer’s job.

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
