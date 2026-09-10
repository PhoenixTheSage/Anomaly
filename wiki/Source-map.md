# Source map

Open these from the [Anomaly repo](https://github.com/PhoenixTheSage/Anomaly) while you work. Types below are well-known names for reflection.

## Docs

| File | Role |
|------|------|
| [Docs/ShaderAPI.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ShaderAPI.md) | Architecture, layers, stage list, composition |
| [Docs/ShaderPacks.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ShaderPacks.md) | Pulsar assets, pack layout, security, terminal pages |
| [Docs/Extensibility.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Extensibility.md) | Slices M–Z, AA–AF, and AG (inject, owned passes, fullscreen, color bus) |
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
| `ClientPlugin.Shaders.FullscreenPassRegistry` | `SetUniforms` / `SetEnabled` / `RequestSrv` / `RequestLitMips` / data-driven `Fullscreen/` draws |
| `ClientPlugin.Buffers.BufferCatalog` | `Active` / `Publish` / `RegisterLifetime` / `RequestLitMips` |
| `ClientPlugin.Shaders.TonemapInputs` | Captured bloom / avgLum / dirt flags from `MyToneMapping.Run` |
| `ClientPlugin.Shaders.FullscreenCompose` | IsolatedAdd / Replace / Chain / … |
| `ClientPlugin.Shaders.FrameTemporal` | `JitterX`/`Y`, `UnjitteredViewProj`, `SafetyScale`, `InvalidateHistory` |
| `ClientPlugin.ShaderFramework.RenderTrace` | `Begin`/`End` interned names; `Dump`/`DumpIfLost` on lost-device (writes `SpaceEngineers.log`) |
| `ClientPlugin.Buffers.PublishedBuffer` | `ISharedBuffer` wrapper for `Publish` |
| `ClientPlugin.Velocity.VelocityRegistry` | `Active` |
| `ClientPlugin.RichHud.TerminalConfigRegistry` | `RequestPage` / `RequestFolderPage` / `Label` / `Checkbox(..., enabled)` under **Anomaly Shaders** |

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
| [FullscreenPassRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FullscreenPassRegistry.cs) | Pack `Fullscreen/` compile + merge |
| [FullscreenHlslLint.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FullscreenHlslLint.cs) | Warn on `Fullscreen/` `while` / uncapped `for` |
| [TemporalParticipation.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/TemporalParticipation.cs) | `reactiveMask` clear / IsolatedAdd luma stamp / ContributeVelocity |
| [ReactiveStamp.hlsl](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/ReactiveStamp.hlsl) | Dilated isolated luma → `reactiveMask` |
| [AnomalyFullscreen.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/AnomalyFullscreen.hlsli) | Fullscreen bus t0–t9 / b6 / b7 |
| [FrameTemporal.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FrameTemporal.cs) | Jitter + unjittered VP + `SafetyScale` |
| [CameraVelocityPass.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/CameraVelocityPass.cs) | Camera MV |
| [Anomaly.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly.hlsli) | Geometry velocity CB / prev-world (VS reconstruct) |
| [LightingSlots.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly/LightingSlots.hlsli) | Lighting t5 / b6 |
| [AtmosphereSlots.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly/AtmosphereSlots.hlsli) | Atmosphere t6 / b6 (t5 is DensityLut) |
| [Light.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Lighting/Light.hlsli) | Thin `Keen/` wrap + extras |
| [AtmosphereCommon.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Transparent/Atmosphere/AtmosphereCommon.hlsli) | Thin `Keen/` wrap + extras |
| [TerminalConfigRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/TerminalConfigRegistry.cs) | Rich HUD **Anomaly Shaders** pages |
| [AnomalyTerminalPages.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/RichHud/AnomalyTerminalPages.cs) | Mirrors Pulsar Settings + Velocity Debug |
