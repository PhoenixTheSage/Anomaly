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

Direction revision: `CurrentToPrevious = 8`, combined flags **15** (legacy flags 7 emitted current-minus-previous). `previousPixel = currentPixel + motion`. A surface moving 3 pixels right emits (-3, 0). Consumers must recognize the direction flag: direct NGX uses (+1,+1) for this revision and (-1,-1) for legacy Anomaly. Do not negate twice. This change does not certify numerical accuracy.

- Texture: render-resolution `RG16F` (or `RGBA16F` if a fourth channel is needed later).
- Units: **pixel delta** at internal (DRS) resolution.
- Y-down D3D (top of the RT is v = 0).
- Unjittered view-projections. Jitter is a consumer problem.
- `MatchesRenderResolution` means this buffer's `Width`/`Height`, not the DXGI swapchain. Lighting can `Load` at `svPos.xy`. Post-CopyToRT / swapchain consumers (frame generation) must **UV-sample** and convert `mvUv = mvPx / float2(Width, Height)`. Do not require `Width == Backbuffer`.

A TAA/DLSS plugin is the 1:1 internal-res consumer. A display-sized interpolator that treats pixel delta as output pixels will warp silhouettes.

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

Anomaly can overlay this texture in-game (**Velocity Debug** → **Debug view** → `Velocity`) so you can compare `GBuffer` vs `CameraOnly` without a frame debugger. Complementary-depth 0 (sky) is dark grey in that overlay so still meshes silhouette; the published RG16F still camera-fills those pixels.

Transparent, glass / holo / shield, foliage, and deferred decals do not write Target3 today. They receive the same camera-from-depth fill as sky. That leftover coverage is near-future [Slice M](../../Docs/ROADMAP.md#slice-m--non-gbuffer-velocity-coverage-near-future), not a consumer contract change.

Named buffers that are not velocity-specific use `ClientPlugin.Buffers.BufferCatalog` — see [Buffers/README.md](../Buffers/README.md). `Active("velocity")` is the same producer as `VelocityRegistry.Active`.
