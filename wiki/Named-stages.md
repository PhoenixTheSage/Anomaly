# Named stages

Public API is semantic names, not 215 Keen paths. Implemented in `ClientPlugin.Shaders.ShaderStages`. After apply, Anomaly compiles a sentinel for each live named stage and rolls back the pack that breaks it.

> **Caution — Depth.** Depth replacements must stay 0-target. A fourth MRT on Depth breaks shadows. Inject into Depth is rejected.

## Geometry

| Stage | Keen mapping | Notes |
|-------|--------------|-------|
| GBuffer | Shared GBuffer pass + `GBufferWrite` / `GBuffer.hlsli` | Velocity Target3 lives here. Not a Materials fork. |
| Depth | Depth pass stages | No extra MRT. |
| Forward | Probe / far forward | |
| Highlight | Selection outline | |
| Transparent | OIT pass + resolve | |
| TransparentForDecals | Glass receiving decals | |

## Lighting and post

| Stage | Keen mapping |
|-------|--------------|
| Lighting.Dir / .Point / .Spot | Deferred lighting programs |
| Post.Tonemap | `Postprocess/Tonemapping/Main.hlsl` |
| Post.HBAO | `Postprocess/HBAO/*` |
| Post.SSAO | `Postprocess/SSAO/Ssao.hlsl` |
| Post.Bloom | `Postprocess/Bloom/*` |
| Post.FXAA | `Postprocess/Fxaa.hlsl` |
| Post.EyeAdaptation | `Postprocess/EyeAdaptation/*` |
| Post.Luminance | `Postprocess/LuminanceReduction/*` |
| Post.ChromaticAberration | `Postprocess/ChromaticAberration/*` |

## Extra Keen stages

| Stage | Keen mapping | Sentinel |
|-------|--------------|----------|
| Shadows | `Shadows/Shadows.hlsl`, `Csm.hlsli`, … | `cs_5_0 Shadows.hlsl` |
| Atmosphere | `Transparent/Atmosphere/*` (Common wrap + extras at t6) | `ps_5_0 AtmosphereGBuffer LQ` |
| Decals | `Decals/Decals.hlsl` | vs + ps, no `RENDER_TO_TRANSPARENT` |
| GPUParticles | `Transparent/GPUParticles/*` | ps `STREAKS=0;LIT_PARTICLE=0` |
| EnvProbe | `EnvProbe/*` | ps `EnvProbeBlend.hlsl` |
| Foliage | `Foliage/*` | ps `Foliage.hlsl` |

Decals and foliage include `GBufferWrite`. Anomaly does not add `ANOMALY_VELOCITY` on those compiles (no `RENDERING_PASS`). Deferred-decal velocity coverage is still a hole.

## Anomaly-owned stages

| Stage | Files |
|-------|-------|
| Anomaly.CameraVelocity | `CameraVelocity.hlsl`, `Fullscreen.hlsl` |
| Anomaly.LinearDepth | `LinearDepth.hlsl`, `HiZDownsample.hlsl`, `Fullscreen.hlsl` |
| Anomaly.HistoryColor | `HistoryCopy.hlsl`, `Fullscreen.hlsl` |
