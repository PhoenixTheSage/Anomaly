# Velocity contract

Typed convenience for motion vectors. Catalog `Active("velocity")` aliases the same producer as `VelocityRegistry.Active`. Prefer the registry if you need convention flags.

| Field | Meaning |
|-------|---------|
| `Srv` / `NativeResource` | Keen SRV or `ID3D11Resource*` |
| `Width` / `Height` | Internal (DRS) size |
| `Convention` | Unjittered, PixelSpace, MatchesRenderResolution, CurrentToPrevious (8) |
| `HistoryValid` | False on first frame, camera cut (~80 m), or resize |

## Units

RG16F pixel delta at internal resolution. Y-down D3D (top of the RT is v = 0). Debug overlay: complementary-depth 0 (sky / uncleared GBuffer; Keen `IsDepthForeground` is `hw > 0`) is dark grey so still meshes silhouette; geometry rest is mid-gray (R/G signed delta around 0.5, B = 0.5 + speed); magenta is invalid history on geometry. The catalog texture still camera-fills sky; the overlay ignores those MVs.

## Settings (in-game proof)

Velocity source: GBuffer (object motion on Keen GBuffer pixels, camera fill on complementary-depth 0 / sky) or CameraOnly (fullscreen depth reprojection). The GPU never sees ActorIDs. CPU history is keyed by `MyInstance.ActorID` / `IMyActor.ID`. Stage 2 packs those worlds into t15 in instance-buffer order; the VS reads `t15[SV_InstanceID + InstanceBase]` (`InstanceBase` is the group's `OffsetInInstanceBuffer`). Player ships are old-pipeline cube instancing: VS b6 `HasPrevWorld` is CPU `currToPrev` from last Main-view object-CB rows (not ViewId 0). The main settings page exposes the producer and routine framework status. Velocity Debug opens the separate visualization/proofing page; Debug view overlays the catalog at the presented size, Debug scale maps motion to color, and Velocity Status reports output/history plus only the currently enabled proofing. The full-resolution audit target exists only while `VelocityPipelineAudit` is selected; persistence textures exist only while persistence is enabled.

> **Note — Runtime probe ladder.** `GBufferVelocityRaw` + `TargetClear` proves the clear. `MrtWrite` clears to -X/cyan, makes the active velocity VS and final GBuffer PS emit +X/pink, and gives Target3 an RG-replace blend while preserving Keen’s other MRTs. The PS probe uses a dedicated immutable 16-byte b7 payload, independent from the 240-byte VS b6 layout. Gray means the shader explicitly wrote zero; matching sentinel-colored regions did not write Target3. `PassEndClear` issues +X once on Keen’s immediate context after every deferred geometry list executes. All foreground pixels must then be pink and `pass-end-clear` must be 1. `HistoryCoverage` shows previous-world hits versus misses, and `Off` restores real motion. Probe changes are immediate and never rebuild shaders. The one startup refresh uses Keen’s `ReloadEffects` render message and should reach `resident=done#N`. The same pack fingerprint and Keen patch status does not queue another refresh.

> **Note — Four-panel velocity pipeline audit.** `VelocityPipelineAudit` proves four boundaries in one frame without a second geometry renderer. Top-left is the live Target3 value, top-right is green when the injected GBuffer pixel shader executes (red means its Target7 execution bit stayed zero), bottom-left is magenta when that shader reads the enabled PS b7 probe, and bottom-right displays the untouched VS-to-PS velocity semantic. The sideband proof is written to internal Target7 during Keen’s existing geometry draw. Developer status also reads back the native blend object, independent-blend flag, and Target3/Target7 write masks at each draw boundary.

> **Note — Near-future: non-GBuffer Target3 holes.** Camera fill already covers sky. Transparent, glass / holo / shield, foliage, and deferred decals still never write Target3, so they ghost like CameraOnly. That coverage is roadmap Slice M — not a consumer contract change, and not a fourth target on Depth.

> **Note — NVIDIA consumer flags.** Current-to-previous pixels: `previousPixel = currentPixel + motion`. Direct NGX scale +1/+1; legacy flags 7 need -1/-1. `MVJittered` off, `MVLowRes` on. These are consumer flags, not Anomaly create flags.

```csharp
var registry = assembly.GetType("ClientPlugin.Velocity.VelocityRegistry");
var active = registry?.GetProperty("Active")?.GetValue(null);
// IVelocityBuffer — if null or !IsAvailable, keep camera-only fallback
```

→ [[Owned-passes|When the pass runs]]
