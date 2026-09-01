# Velocity contract

Anomaly publishes a per-frame velocity texture. Consumers (SE-DLSS, later TAA / motion blur) **must not** take a compile-time project reference. Resolve the well-known types by name on loaded assemblies, then read `VelocityRegistry.Active` **before the first draw** that needs motion vectors.

## Types (`ClientPlugin.Velocity`)

| Type | Role |
|------|------|
| `IVelocityBuffer` | `IsAvailable`, Keen `ISrvBindable` as `object Srv`, native `ID3D11Resource*` (`NativeResource`), `Width`/`Height`, `Convention`, `HistoryValid` |
| `VelocityRegistry` | `Active` is this plugin’s built-in producer |
| `VelocityConvention` | Flags: `Unjittered`, `PixelSpace`, `MatchesRenderResolution` |
| `IVelocityHistory` | ActorID snapshot for implementors only |

NVIDIA flags for consumers (not Anomaly create flags): `MVJittered` off, `MVLowRes` on.

## Convention

- Texture: render-resolution `RG16F` (or `RGBA16F` if a fourth channel is needed later).
- Units: **pixel delta** at internal (DRS) resolution.
- Y-down D3D (top of the RT is v = 0).
- Unjittered view-projections. Jitter is a consumer problem.

## Discovery (C# sketch)

```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    var registry = assembly.GetType("ClientPlugin.Velocity.VelocityRegistry");
    if (registry == null)
        continue;
    var active = registry.GetProperty("Active")?.GetValue(null);
    // active is IVelocityBuffer; check IsAvailable, then bind Srv / NativeResource
    break;
}
```

If `Active` is null or `IsAvailable` is false, keep a camera-only fallback. Do not Harmony-patch instance updates from the consumer.

Anomaly can overlay this texture in-game (**Debug velocity** in settings) so you can compare `GBuffer` vs `CameraOnly` without a frame debugger.

Named buffers that are not velocity-specific use `ClientPlugin.Buffers.BufferCatalog` — see [Buffers/README.md](../Buffers/README.md). `Active("velocity")` is the same producer as `VelocityRegistry.Active`.
