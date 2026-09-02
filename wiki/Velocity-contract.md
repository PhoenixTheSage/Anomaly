# Velocity contract

Typed convenience for motion vectors. Catalog `Active("velocity")` aliases the same producer as `VelocityRegistry.Active`. Prefer the registry if you need convention flags.

| Field | Meaning |
|-------|---------|
| `Srv` / `NativeResource` | Keen SRV or `ID3D11Resource*` |
| `Width` / `Height` | Internal (DRS) size |
| `Convention` | Unjittered, PixelSpace, MatchesRenderResolution |
| `HistoryValid` | False on first frame, camera cut (~80 m), or resize |

## Units

RG16F pixel delta at internal resolution. Y-down D3D (top of the RT is v = 0). Mid-gray in the debug overlay is no motion; magenta is invalid history.

## Settings (in-game proof)

Velocity source: GBuffer (object motion on Keen pixels, camera fill on sky/particles) or CameraOnly (fullscreen depth reprojection). Debug buffer overlays the catalog. Debug scale (px) maps motion to color.

> **Note — NVIDIA consumer flags.** For DLSS-class APIs: MVJittered off, MVLowRes on. Those are consumer flags, not Anomaly create flags.

```csharp
var registry = assembly.GetType("ClientPlugin.Velocity.VelocityRegistry");
var active = registry?.GetProperty("Active")?.GetValue(null);
// IVelocityBuffer — if null or !IsAvailable, keep camera-only fallback
```

→ [[Owned-passes|When the pass runs]]
