# HLSL cookbook

Packs should almost never compile Keen permutations themselves. Inject into extras, overlay a named stage, or ship a `Fullscreen/<Slot>` pixel shader.

## GBuffer pixel helper (inject)

`Inject/GBuffer.hlsli` — visible from PixelStage / GBufferWrite.

```hlsl
#ifdef ANOMALY_PIXEL_STAGE
// Runs on GBuffer PS only. Depth never sees this file.
#endif
```

Velocity reconstruct (`AnomalyComputeVelocity`) is VS-only. PixelStage defines `ANOMALY_PIXEL_STAGE` so GBuffer PS can include `Anomaly.hlsli` for extras without Keen’s `construct_matrix_43`. Packed t15 / CB rows are previous world (camera-relative 4x3). The VS inverts current `local_matrix` — packing `currToPrev` on the CPU showed up as Thread CPU Load (`Parallel.Scheduler`).

`GbufferWrite` / `GbufferWriteBlend` match Keen’s argument list unless `ANOMALY_VELOCITY` is set. Do not pass a velocity argument from Decals or foliage overlays.


## Sample velocity from lighting (inject)

`Inject/Lighting.hlsli` — `Light.hlsli` includes `Anomaly/Extras/Lighting.hlsli`. t5 is bound by Anomaly.

```hlsl
#ifdef ANOMALY_VELOCITY
float2 mv = AnomalyVelocityBuffer[uint2(svPos.xy)].xy;
if (AnomalyLightingHasVelocity) { /* … */ }
#endif
```

## Atmosphere extras (inject)

`Inject/Atmosphere.hlsli` — wrap includes `Anomaly/Extras/Atmosphere.hlsli`. Velocity is t6. `DensityLut` stays t5.

```hlsl
#ifdef ANOMALY_ATMOSPHERE_STAGE
float2 mv = AnomalyVelocityBuffer[uint2(svPos.xy)].xy;
float2 jitter = AnomalyLightingJitter;
#endif
```

## Sample an extra attachment

```hlsl
#ifdef ANOMALY_ATTACH_OBJECTID
uint id = AnomalyAttach_objectid[uint2(svPos.xy)].r;
#endif
```

## Pack defines

`anomaly.json` `defines` merge onto GBuffer and lighting (never Depth). Same define from two packs is fine. Reserved — packs cannot set these: `ANOMALY`, `ANOMALY_VELOCITY`, `RENDERING_PASS`, `DEPTH_ONLY`, `CUSTOM_DEPTH`, `ANOMALY_ATTACH_*`.

## AfterAtmosphere curtain (fullscreen)

`Fullscreen/AfterAtmosphere/Curtain.hlsl` — Anomaly compiles and draws this. Do not create an RT.

```hlsl
#include <AnomalyFullscreen.hlsli>

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float2 mv = AnomalyVelocityBuffer[uint2(pos.xy)].xy;
    float intensity = AnomalyPassUniform0.x;
    return float4(0.02, 0.05, 0.12, 1) * intensity;
}
```

Folder default is IsolatedAdd into `LBuffer`. Set `temporal: ["InColor","Reactive"]` (and contribute MVs from C# if the curtain animates) or DLSS will ghost. AfterAtmosphere cannot sample Keen `DensityLut` (already unbound).

## Overlay Post.Tonemap

Drop `Overlay/Post.Tonemap/Main.hlsl`. Unique basename maps to Keen’s file. If compile fails, that pack rolls back; Keen tonemap returns.

## Includes

System includes use angle brackets so they search Anomaly’s include dir: `#include <Anomaly.hlsli>`. Quoted includes with a Keen base path do not fall through if missing.

→ [[Overlay-vs-inject|Mapping rules]] · [[Fullscreen-programs|Fullscreen programs]] · [[GBuffer-attachments|Request a target]]
