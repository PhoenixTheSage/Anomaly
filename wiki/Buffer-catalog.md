# Buffer catalog

Vulkan CPU-backed PublishedBuffer uploads retain cloned RGBA textures, volume mip chains and structured floats across device replacement. Device-end callbacks run before reupload; failed callbacks block guest submission for retry. Retained GuestRendererImage wrappers expire with their native resource epoch. Context teardown/recreation GPU coverage includes exact typed reads of restored payloads and fresh volume/occupancy/spectator-shadow output; loaded-world device-loss and visual acceptance remain separate.

Resolve `ClientPlugin.Buffers.BufferCatalog` by type name. `Active(name)` never returns null — check `IsAvailable`. `Srv` is Keen `ISrvBindable` boxed as object.

| Name | When it exists | Format |
|------|----------------|--------|
| `velocity` | After the depth-equal object pass and the camera fill | RG16F, internal resolution. Non-zero object motion is kept. Sky, skinned characters, voxels, foliage, and decals are camera fill. DX11 still writes object motion on GBuffer `SV_Target3`. |
| `linearDepth` | After scheduler Done, only if a pack is live or Debug buffer is Linear depth / Hi-Z | R32_Float, full res, positive view Z |
| `hiZ` | Right after linearDepth, only if a pack is live or Debug buffer is Hi-Z | R32_Float, half res, 2×2 min (not GenerateMips) |
| `historyColor` | After DrawGameScene postfix, only if a pack is live or Debug buffer is History color | RGBA16F HDR LBuffer copy; unpublished until one copy |
| `reactiveMask` | When an owned pass or IsolatedAdd program sets `TemporalPolicy.Reactive` | R8, full res; IsolatedAdd auto-stamps dilated luma **on the slot `rc`** (transparent deferred worker for AfterAtmosphere). Anomaly redirects `MyRender11.RC` during those callbacks. White = do not trust history |
| `objectId` (or any attachment name) | If a pack requested it | Pack format. DX11: `GBufferAttachments.TryGet`, `SV_Target4`–`6`. Vulkan: catalog `Active(name)`, sentinel `0xFFFF000000000009` plus the slot (`SV_Target4` is slot 0). |
| `fullscreenIsolated` | After a `Fullscreen/` program runs | Last isolated RT (slot res, or 1/2 / 1/4 when that program set `scale`); reserved |
| `hdrColor` | When GBuffer exists | Aliases Keen `LBuffer` (internal HDR) |
| `pointLights` | After `PreparePointLights` when point lights are on and last-frame visible | Structured 48-byte `AnomalyPointLight`; `Width` = element count, `Height` = 1. Wrap, no copy. Dummy 1-element SRV on HDR t10 when empty |
| `tileIndices` | Same as `pointLights` | Structured `uint`; 16×16 tiles then per-tile light indices. Dummy 1-element SRV on HDR t11 when empty |
| `litMips` | AfterLighting, only if a live fullscreen program bound `litMips`, `RequestLitMips` was called, or Debug buffer is LitMips | RGBA16F, full res, default 5 mips of this-frame `LBuffer` (before atmosphere). Reserved |
| `historyDepth` | After scheduler Done, once two linear frames exist | R32_Float unread linear ping-pong (last frame’s view Z). Reserved |
| `occupancy` | AfterLighting after BeforeFullscreen, if `RequestOccupancy`; Vulkan requires current-frame depth and native packet features | R8 on DX11, R32F on Vulkan; 512×512 atlas of a 64³ camera-relative clipmap (2 m voxels). Production depth splat + bounded actor AABB stamps. Reserved |
| `pointShadowAtlas` | DX11 and Vulkan AfterLighting, if `RequestPointShadows(cap > 0)` | RGBA32F skinned light-view mesh plus optional AABB depth. Width = faceRes×6, height = 1 + faceRes×allocated lights. Row 0 retains viewPos.xyz/range and caster validity. Default cap 4 / face 128, max 64 / 1024, bounded by dimensions and roughly 96 MiB. Reserved |
| `playerDepth` | DX11 and Vulkan with the requested character atlas | R32F render-sized Euclidean depth of the current character LOD, far-cleared when absent. Reserved |
| `upscaledColor` | After `NotifyUpscaleComplete(rc, color)` | Unique upscale dest at output res. Cleared next frame. Reserved |
| `pass.<id>` | Same draw | That program’s isolated output (scaled when `passes[].scale` is 0.5 or 0.25) |
| `volumeSunShadow` | After a PublishOnly program with `output: volumeSunShadow` | RGBA remaining sun (`.r` = 1 none). `.a < 0.5` is missing. Not reserved. Helper `AnomalyVolumeSunShadow` |

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

`RequestLitMips` / `RequestOccupancy` / `RequestPointShadows(maxLights, faceResolution)` / `RequestWorldBoxes` are C# asks on `BufferCatalog` (also forwarded from `FullscreenPassRegistry`). Cube cap default **4**, max **64**; face default **128**, max **1024**, bounded by dimensions and roughly 96 MiB. `historyDepth` needs no request — it is the unread linear ping-pong after the second frame.

## Publish (pack-owned names)

`BufferCatalog.Publish(packId, name, buffer)` / `Unpublish` / `UnpublishAll`. Reserved names fail closed. Two pack ids on the same name fail closed. Use `PublishedBuffer` as the `ISharedBuffer`. `PublishedBuffer.Publish` is **2D only** (unique name — do not `GetMethod("Publish")` if Anomaly later adds overloads; bind the four-arg signature). Texture3D is `Publish3D`; UAV bake is `PublishBake`; node pools are `PublishStructured`. `RegisterLifetime` is DRS / device-end — drop your own `OnDeviceReset` Harmony.

> **Warning — Jitter.** SE-DLSS owns Halton jitter (Projection M31/M32). Anomaly reads it into `FrameTemporal` and republishes an unjittered VP on the extras CB. Linearize uses M33/M43 only. Do not steal jitter.

→ [[Velocity-contract|Velocity-specific flags]] · [[Frame-graph|When buffers freeze]]

## Vulkan render-target requests (2026-10-21 candidate)

`CatalogImages.EnsureColorTarget(packId, name, width, height, format)` now retains
the requested DXGI format (2/10/28/34/41/54), defers device work until the renderer
callback and reuses its image ID on resize. An identical ensure preserves image
contents. `ClearColorTarget` queues a floating-point clear, including FP16 alpha
-1 and HDR values. Names have one target owner; a conflicting owner is refused.
Unpublish or unregister queues native retirement. Prepare target allocation and
retirement before queuing frame passes. New operations require SE-Vulkan native
feature bit13; old hosts return an actionable update refusal.
