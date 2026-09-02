# Buffer catalog

Resolve `ClientPlugin.Buffers.BufferCatalog` by type name. `Active(name)` never returns null — check `IsAvailable`. `Srv` is Keen `ISrvBindable` boxed as object.

| Name | When it exists | Format |
|------|----------------|--------|
| `velocity` | After GBuffer / camera pass | RG16F, internal resolution |
| `linearDepth` | After scheduler Done, only if a pack is live or Debug buffer is Linear depth / Hi-Z | R32_Float, full res, positive view Z |
| `hiZ` | Right after linearDepth, only if a pack is live or Debug buffer is Hi-Z | R32_Float, half res, 2×2 min (not GenerateMips) |
| `historyColor` | After DrawGameScene postfix, only if a pack is live or Debug buffer is History color | RGBA16F HDR LBuffer copy; unpublished until one copy |
| `reactiveMask` | When an owned pass sets `TemporalPolicy.Reactive` | R8, full res; white = do not trust history |
| `objectId` (or any attachment name) | If a pack requested it | Pack format; also `GBufferAttachments.TryGet` |
| `fullscreenIsolated` | After a `Fullscreen/` program runs | Last isolated RT; reserved |
| `pass.<id>` | Same draw | That program’s isolated output |

```csharp
foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
{
    var catalog = assembly.GetType("ClientPlugin.Buffers.BufferCatalog");
    if (catalog == null) continue;
    var active = catalog.GetMethod("Active")?.Invoke(null, new object[] { "linearDepth" });
    // ISharedBuffer: IsAvailable, Srv, NativeResource, Width, Height
    break;
}
```

## Publish (pack-owned names)

`Publish(packId, name, buffer)` / `Unpublish` / `UnpublishAll`. Reserved names fail closed. Two pack ids on the same name fail closed. Use `PublishedBuffer` as the `ISharedBuffer`. `RegisterLifetime` is DRS / device-end — drop your own `OnDeviceReset` Harmony.

> **Warning — Jitter.** SE-DLSS owns Halton jitter (Projection M31/M32). Anomaly reads it into `FrameTemporal` and republishes an unjittered VP on the extras CB. Linearize uses M33/M43 only. Do not steal jitter.

→ [[Velocity-contract|Velocity-specific flags]] · [[Frame-graph|When buffers freeze]]
