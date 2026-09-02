# Source map

Open these from the [Anomaly repo](https://github.com/PhoenixTheSage/Anomaly) while you work. Types below are well-known names for reflection.

## Docs

| File | Role |
|------|------|
| [Docs/ShaderAPI.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ShaderAPI.md) | Architecture, layers, stage list, composition |
| [Docs/ShaderPacks.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/ShaderPacks.md) | Pulsar assets, pack layout, security |
| [Docs/Extensibility.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Extensibility.md) | Slices M–Z and AA–AF (inject, owned passes, fullscreen programs) |
| [Docs/PLAN.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/PLAN.md) | Why velocity, Keen frame order |
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
| `ClientPlugin.Shaders.OwnedPassRegistry` | `Register` / `NotifyUpscaleComplete` |
| `ClientPlugin.Shaders.FullscreenPassRegistry` | `SetUniforms` / data-driven `Fullscreen/` draws |
| `ClientPlugin.Shaders.FullscreenCompose` | IsolatedAdd / Replace / Chain / … |
| `ClientPlugin.Shaders.FrameTemporal` | `JitterX`/`Y`, `UnjitteredViewProj`, `InvalidateHistory` |
| `ClientPlugin.Buffers.BufferCatalog` | `Active` / `Publish` / `RegisterLifetime` |
| `ClientPlugin.Buffers.PublishedBuffer` | `ISharedBuffer` wrapper for `Publish` |
| `ClientPlugin.Velocity.VelocityRegistry` | `Active` |

## Implementation

| File | Role |
|------|------|
| [ShaderCompileIntercept.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/ShaderCompileIntercept.cs) | Include dirs + macros |
| [ShaderStages.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/ShaderStages.cs) | Stage files + sentinels |
| [OwnedBuffersPass.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/OwnedBuffersPass.cs) | `linearDepth` / `hiZ` / history |
| [OwnedPassRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/OwnedPassRegistry.cs) | Pack draw slots |
| [FullscreenPassRegistry.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FullscreenPassRegistry.cs) | Pack `Fullscreen/` compile + merge |
| [AnomalyFullscreen.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/AnomalyFullscreen.hlsli) | Fullscreen bus t0–t3 / b6 / b7 |
| [FrameTemporal.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/FrameTemporal.cs) | Jitter + unjittered VP |
| [CameraVelocityPass.cs](https://github.com/PhoenixTheSage/Anomaly/blob/main/ClientPlugin/ShaderFramework/CameraVelocityPass.cs) | Camera MV |
| [Anomaly.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly.hlsli) | Geometry velocity CB / prev-world |
| [LightingSlots.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly/LightingSlots.hlsli) | Lighting t5 / b6 |
| [AtmosphereSlots.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Anomaly/AtmosphereSlots.hlsli) | Atmosphere t6 / b6 (t5 is DensityLut) |
| [AtmosphereCommon.hlsli](https://github.com/PhoenixTheSage/Anomaly/blob/main/Assets/Shaders/Transparent/Atmosphere/AtmosphereCommon.hlsli) | Thin `Keen/` wrap + extras |
