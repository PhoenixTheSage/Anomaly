# Source map

Open these from the [Anomaly repo](https://github.com/PhoenixTheSage/Anomaly) while you work. Types below are well-known names for reflection.

## Docs

| File | Role |
|------|------|
| [Docs/ShaderAPI.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ShaderAPI.md) | Architecture, layers, stage list, composition |
| [Docs/ShaderPacks.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ShaderPacks.md) | Pulsar assets, pack layout, security, terminal pages |
| [Docs/Extensibility.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Extensibility.md) | Slices M–Z, AA–AM (HDR illuminant / IsolatedMix energy / march helper / planet air / local mesh map / planet-night AJ×0.05 + monotonic sun transmittance) |
| [wiki/Framework-gaps.md](Framework-gaps.md) | Pack workarounds Anomaly should own (where / how / why) |
| [Docs/PLAN.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/PLAN.md) | Why velocity, Keen frame order |
| [Docs/ROADMAP.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ROADMAP.md) | Slices; Slice M is leftover Target3 coverage |
| [Docs/KeenShaders.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/KeenShaders.md) | Inventory of Keen HLSL |
| [ClientPlugin/Buffers/README.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/Buffers/README.md) | Catalog names, jitter contract |
| [ClientPlugin/Velocity/README.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/Velocity/README.md) | `IVelocityBuffer` convention |

## Well-known types

| Type | Call |
|------|------|
| `ClientPlugin.Shaders.ShaderPackRegistry` | `Register(id, root)` |
| `ClientPlugin.Shaders.ShaderStages` | Named stage table |
| `ClientPlugin.Shaders.GBufferAttachments` | `Request(name, format)` |
| `ClientPlugin.Shaders.ShaderBindRegistry` | `RequestSrv(stage, catalogName)` |
| `ClientPlugin.Shaders.OwnedPassRegistry` | `Register` / `ClaimUpscale` / `NotifyUpscaleComplete(rc, color)` / `HasDisplayTenant` |
| `ClientPlugin.Shaders.FullscreenPassRegistry` | `SetUniforms` / `SetEnabled` / `SetScale` / `TryGetOutputSize` / `TryGetProgramStatus` / `RequestSrv` / `RequestLitMips` / `RequestPointShadows` / `RequestOccupancy` / data-driven `Fullscreen/` draws |
| `ClientPlugin.Buffers.BufferCatalog` | `Active` / `Publish` / `RegisterLifetime` / `RequestLitMips` / `RequestPointShadows` / `RequestOccupancy` |
| `ClientPlugin.Shaders.TonemapInputs` | Captured bloom / avgLum / dirt flags from `MyToneMapping.Run` |
| `ClientPlugin.Shaders.FullscreenCompose` | IsolatedAdd / IsolatedMix / IsolatedSub / Replace / Chain / … |
| `ClientPlugin.Shaders.FrameTemporal` | `JitterX`/`Y`, `UnjitteredViewProj`, `SafetyScale`, `SunColor` / `SunToward` / `SunDiffuse` / `SkyLuma` / `SkyAmbient` / `PlanetAirTop` / `VisualAtmoCeil` / `VolumeSkipFloor` / `VolumeSkipMul`, `InvalidateHistory` |
| `ClientPlugin.Shaders.PlanetAtmosphere` | `TryGetRadii(worldCenter, matchMeters, out airTop, out visualCeil)` / `TryComputeRadii` — game thread; radii from planet center |
| `ClientPlugin.Shaders.LocalCharacter` | Game-thread local player actor id, first-person flag, and cam-rel AABB (`TryGetCamRelBox`) for mesh maps; 0 otherwise |
| `ClientPlugin.ShaderFramework.RenderTrace` | `Begin`/`End` interned names; `Dump`/`DumpIfLost` on lost-device (writes `SpaceEngineers.log`) |
| `ClientPlugin.Buffers.PublishedBuffer` | `ISharedBuffer` wrapper: `Publish` (2D), `Publish3D`, `PublishBake`, `PublishStructured` |
| `ClientPlugin.Velocity.VelocityRegistry` | `Active` |
| `ClientPlugin.RichHud.TerminalConfigRegistry` | `RequestPage` / `RequestFolderPage` (folder+page or page+folder path) / `Label` / `Checkbox(..., enabled)` / `OnResized` / `RegisterWindowResized` under **Anomaly Shaders** |
| `ClientPlugin.RichHud.HudOverlayRegistry` | `Register(id, get)` / `Unregister` corner status when Master is registered |
| `ClientPlugin.RichHud.ResizableWindow` | Anomaly-owned `WindowBase` subclass; `Resizing` / `Resized` from `resizeDir` |
| `ClientPlugin.RichHud.TerminalWindowLayout` | `ColumnsFor` / `ContentWidth` (preferred ~300px **internal** columns inside one full-width tile; `SeparateAt` / `Columns(n)` override) |

## Implementation

| File | Role |
|------|------|
| [ShaderCompileIntercept.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/ShaderCompileIntercept.cs) | Include dirs + macros + Keen patch serve |
| [KeenShaderGuard.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/KeenShaderGuard.cs) | Hash + patch Keen GBuffer files at load |
| [ShaderStages.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/ShaderStages.cs) | Stage files + sentinels |
| [OwnedBuffersPass.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/OwnedBuffersPass.cs) | `linearDepth` / `hiZ` / history / `litMips` |
| [MainViewGate.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/MainViewGate.cs) | Skip HDR slots on LCD / TargetView / TargetCamera |
| [OwnedPassRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/OwnedPassRegistry.cs) | Pack draw slots |
| [RenderTrace.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/RenderTrace.cs) | Last-N GPU-submit breadcrumbs; dump on lost-device |
| [FullscreenPassRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FullscreenPassRegistry.cs) | Pack `Fullscreen/` compile + merge (IsolatedSub `dest*(1-src)` blend onto dest) |
| [PointLightCatalog.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/PointLightCatalog.cs) | Wrap Keen tiled `pointLights` / `tileIndices`; dummy SRVs for HDR t10/t11 |
| [PointShadowPass.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/PointShadowPass.cs) | Occupancy clipmap + light-view mesh/AABB atlas (`RequestOccupancy` / `RequestPointShadows` / `RequestWorldBoxes`) |
| [FullscreenHlslLint.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FullscreenHlslLint.cs) | Warn on `Fullscreen/` `while` / uncapped `for` |
| [TemporalParticipation.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/TemporalParticipation.cs) | `reactiveMask` clear / IsolatedAdd luma stamp / IsolatedAdd hit-distance MVs / ContributeVelocity |
| [IsolatedVelocity.hlsl](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/IsolatedVelocity.hlsl) | Isolated.a (meters) → camera MVs at curtain depth |
| [ReactiveStamp.hlsl](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/ReactiveStamp.hlsl) | Dilated isolated luma (IsolatedSub `.a`) → `reactiveMask` |
| [AnomalyFullscreen.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/AnomalyFullscreen.hlsli) | Fullscreen bus t0–t13 / b0 `frame_` via `Frame.hlsli` (HDR slots) / b6 / b7; `ANOMALY_PACK_SRVn_TYPE` for t7–t9 and t12–t13; `AnomalyScenePixel` / `AnomalySceneUvOffset` / `AnomalyLightingUv` / `AnomalyLightingViewPos` / `AnomalyLightingViewPosUnjittered` / `AnomalyViewToLightingUv` / `AnomalyViewToDepthUv` / `AnomalyLightingN` / `AnomalyIgn` / `AnomalyIgnWorld` / `AnomalyIsolatedSub` / `AnomalyIsolatedSubEnergy` / `AnomalyVolumeSunShadow` / `AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade` |
| [MeshDepth.hlsl](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/MeshDepth.hlsl) | Light-space skinned VS + euclidean distance PS (no Depth z-clamp) |
| [FrameTemporal.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FrameTemporal.cs) | Jitter + unjittered VP + `SafetyScale` + sun / sky / planet air extras |
| [PlanetAtmosphere.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/Shaders/PlanetAtmosphere.cs) | Game-thread air-top / visual ceil snapshot |
| [LocalCharacter.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/Shaders/LocalCharacter.cs) | Game-thread local player actor id |
| [CameraVelocityPass.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/CameraVelocityPass.cs) | Camera MV |
| [Anomaly.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly.hlsli) | Geometry velocity CB / prev-world (VS reconstruct) |
| [LightingSlots.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly/LightingSlots.hlsli) | Lighting t5 / b6 |
| [AtmosphereSlots.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly/AtmosphereSlots.hlsli) | Atmosphere t6 / b6 (t5 is DensityLut) |
| [Light.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Lighting/Light.hlsli) | Thin `Keen/` wrap + extras |
| [AtmosphereCommon.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Transparent/Atmosphere/AtmosphereCommon.hlsli) | Thin `Keen/` wrap + extras |
| [TerminalConfigRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/TerminalConfigRegistry.cs) | Rich HUD **Anomaly Shaders** pages |
| [TerminalWindowMonitor.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/TerminalWindowMonitor.cs) | Harmony `resizeDir` sample on Master’s terminal `WindowBase` |
| [ResizableWindow.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/ResizableWindow.cs) | Host HUD `WindowBase` subclass with `Resizing` / `Resized` |
| [HudOverlayRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/HudOverlayRegistry.cs) | Rich HUD corner overlay lines |
| [AnomalyTerminalPages.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/AnomalyTerminalPages.cs) | Mirrors Pulsar Settings + Velocity Debug |
