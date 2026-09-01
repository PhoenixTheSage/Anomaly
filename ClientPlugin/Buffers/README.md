# Buffer catalog

Anomaly publishes named GPU textures so consumers (SE-DLSS, later TAA / motion blur / SSR) **must not** take a compile-time project reference. Resolve the well-known types by name on loaded assemblies.

Velocity stays a typed convenience: bind `ClientPlugin.Velocity.VelocityRegistry.Active` (`IVelocityBuffer`) as today. The catalog `Active("velocity")` aliases the same producer.

## Types (`ClientPlugin.Buffers`)

| Type | Role |
|------|------|
| `ISharedBuffer` | `IsAvailable`, Keen `ISrvBindable` as `object Srv`, `NativeResource`, `Width`/`Height` |
| `BufferCatalog` | `Active(name)` / `Set(name, buffer)` |

Well-known names:

| Name | Status |
|------|--------|
| `velocity` | Live — aliases `VelocityRegistry.Active` |
| `linearDepth` | Reserved — producer not in this slice |
| `objectId` | Reserved — live extra GBuffer attachments also resolve through `Active(name)` via `GBufferAttachments.TryGet` |
| `historyColor` | Reserved — owned pass later (slice S) |

## Discovery (C# sketch)

```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    var catalog = assembly.GetType("ClientPlugin.Buffers.BufferCatalog");
    if (catalog == null)
        continue;
    var active = catalog.GetMethod("Active")?.Invoke(null, new object[] { "velocity" });
    // active is ISharedBuffer; check IsAvailable, then bind Srv / NativeResource
    break;
}
```

SE-DLSS should keep using `VelocityRegistry` / `IVelocityBuffer` (convention flags live there). A second consumer that only needs a texture can bind `linearDepth` the same way once a producer calls `BufferCatalog.Set`.

See also [Velocity/README.md](../Velocity/README.md).
