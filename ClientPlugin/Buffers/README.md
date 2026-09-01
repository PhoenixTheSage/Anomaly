# Buffer catalog

Anomaly publishes named GPU textures so consumers (SE-DLSS, later TAA / motion blur / SSR) **must not** take a compile-time project reference. Resolve the well-known types by name on loaded assemblies.

Velocity stays a typed convenience: bind `ClientPlugin.Velocity.VelocityRegistry.Active` (`IVelocityBuffer`) as today. The catalog `Active("velocity")` aliases the same producer.

## Types (`ClientPlugin.Buffers`)

| Type | Role |
|------|------|
| `ISharedBuffer` | `IsAvailable`, Keen `ISrvBindable` as `object Srv`, `NativeResource`, `Width`/`Height` |
| `BufferCatalog` | `Active(name)` / `Set(name, buffer)` |

Well-known names:

| Name | Status | Format / size |
|------|--------|----------------|
| `velocity` | Live — aliases `VelocityRegistry.Active` | `RG16F`, internal resolution |
| `linearDepth` | Live — positive view-space Z from resolved complementary depth (`Frame.hlsli` `compute_depth`) | `R32_Float`, full res |
| `hiZ` | Live — 2×2 **min** downsample of `linearDepth` (not Keen `GenerateMips`) | `R32_Float`, half res |
| `historyColor` | Live — previous-frame HDR `LBuffer` copy, published **after** post so this frame’s TAA still sees N−1 | `RGBA16F`, full res |
| `objectId` | Live extra GBuffer attachments also resolve through `Active(name)` via `GBufferAttachments.TryGet` | pack-requested |

Linear depth and Hi-Z are filled after GBuffer + lighting (`MyRenderScheduler.Done`). History is copied at `DrawGameScene` postfix (after Keen post). First frame / resize: `historyColor` stays unavailable until one copy exists. MSAA `LBuffer` is resolved before the blit. Do **not** use `MyCopyToRT.Run` for this copy (other plugins may intercept it).

## Jitter contract

SE-DLSS owns Halton jitter (`Projection.M31` / `M32`). Anomaly velocity uses an **unjittered** view-projection (those terms cleared). Linearize uses `Projection.M33` / `M43` only, so jitter does not change `linearDepth`. TAA / SSR plugins must not assume Anomaly owns jitter, and must not steal SE-DLSS’s jitter. If both need a shared temporal index later, that belongs in Anomaly’s extras CB — not a second Harmony patch on the projection.

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
