# Buffer catalog

Resolve `ClientPlugin.Buffers.BufferCatalog` by type name. `Active(name)` never returns null — check `IsAvailable`. `Srv` is Keen `ISrvBindable` boxed as object.

| Name | When it exists | Format |
|------|----------------|--------|
| `velocity` | After GBuffer / camera pass | RG16F, internal resolution |
| `linearDepth` | After scheduler Done, only if a pack is live or Debug buffer is Linear depth / Hi-Z | R32_Float, full res, positive view Z |
| `hiZ` | Right after linearDepth, only if a pack is live or Debug buffer is Hi-Z | R32_Float, half res, 2×2 min (not GenerateMips) |
| `historyColor` | After DrawGameScene postfix, only if a pack is live or Debug buffer is History color | RGBA16F HDR LBuffer copy; unpublished until one copy |
| `reactiveMask` | When an owned pass or IsolatedAdd program sets `TemporalPolicy.Reactive` | R8, full res; IsolatedAdd auto-stamps dilated luma **on the slot `rc`** (transparent deferred worker for AfterAtmosphere). Anomaly redirects `MyRender11.RC` during those callbacks. White = do not trust history |
| `objectId` (or any attachment name) | If a pack requested it | Pack format; also `GBufferAttachments.TryGet` |
| `fullscreenIsolated` | After a `Fullscreen/` program runs | Last isolated RT (slot res, or 1/2 / 1/4 when that program set `scale`); reserved |
| `hdrColor` | When GBuffer exists | Aliases Keen `LBuffer` (internal HDR) |
| `pointLights` | After `PreparePointLights` when point lights are on and last-frame visible | Structured 48-byte `AnomalyPointLight`; `Width` = element count, `Height` = 1. Wrap, no copy. Dummy 1-element SRV on HDR t10 when empty |
| `tileIndices` | Same as `pointLights` | Structured `uint`; 16×16 tiles then per-tile light indices. Dummy 1-element SRV on HDR t11 when empty |
| `litMips` | AfterLighting, only if a live fullscreen program bound `litMips`, `RequestLitMips` was called, or Debug buffer is LitMips | RGBA16F, full res, default 5 mips of this-frame `LBuffer` (before atmosphere). Reserved |
| `historyDepth` | After scheduler Done, once two linear frames exist | R32_Float unread linear ping-pong (last frame’s view Z). Reserved |
| `occupancy` | AfterLighting after BeforeFullscreen, if `RequestOccupancy` | R8 512×512 atlas of a 64³ camera-relative clipmap (2 m voxels). Depth splat + actor AABB stamps. Reserved |
| `pointShadowAtlas` | AfterLighting after BeforeFullscreen, if `RequestPointShadows(cap > 0)` | RGBA32F light-view mesh (skinned MeshDepth) plus optional AABB depth. Width = faceRes×6, height = 1 + faceRes×cap. Row 0 is viewPos.xyz + range. Default cap 4 / face 128, max 64 / 256. VRAM grows with the request. Reserved |
| `upscaledColor` | After `NotifyUpscaleComplete(rc, color)` | Unique upscale dest at output res. Cleared next frame. Reserved |
| `pass.<id>` | Same draw | That program’s isolated output (scaled when `passes[].scale` is 0.5 or 0.25) |

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

`RequestLitMips` / `RequestOccupancy` / `RequestPointShadows(maxLights, faceResolution)` / `RequestWorldBoxes` are C# asks on `BufferCatalog` (also forwarded from `FullscreenPassRegistry`). Cube cap default **4**, max **64**; face default **128**, max **256**; VRAM grows with the request. `historyDepth` needs no request — it is the unread linear ping-pong after the second frame.

## Publish (pack-owned names)

`Publish(packId, name, buffer)` / `Unpublish` / `UnpublishAll`. Reserved names fail closed. Two pack ids on the same name fail closed. Use `PublishedBuffer` as the `ISharedBuffer`. `RegisterLifetime` is DRS / device-end — drop your own `OnDeviceReset` Harmony.

> **Warning — Jitter.** SE-DLSS owns Halton jitter (Projection M31/M32). Anomaly reads it into `FrameTemporal` and republishes an unjittered VP on the extras CB. Linearize uses M33/M43 only. Do not steal jitter.

→ [[Velocity-contract|Velocity-specific flags]] · [[Frame-graph|When buffers freeze]]
