# HLSL cookbook

Packs should almost never compile Keen permutations themselves. Inject into extras, overlay a named stage, or ship a `Fullscreen/<Slot>` pixel shader.

## GBuffer pixel helper (inject)

`Inject/GBuffer.hlsli` — visible from PixelStage / GBufferWrite.

```hlsl
#ifdef ANOMALY_PIXEL_STAGE
// Runs on GBuffer PS only. Depth never sees this file.
#endif
```

Velocity reconstruct (`AnomalyComputeVelocity`) is VS-only. PixelStage defines `ANOMALY_PIXEL_STAGE` so GBuffer PS can include `Anomaly.hlsli` for extras without Keen’s `construct_matrix_43`. Stage 2 t15 is previous world (camera-relative 4x3) at instance-buffer slots; the VS inverts current `local_matrix` and indexes `t15[SV_InstanceID + AnomalyInstanceBase]`. Old-pipeline cube `PrevRow` is CPU `currToPrev` — the VS must not invert `local_matrix` again.

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

`Fullscreen/AfterAtmosphere/Curtain.hlsl` — Anomaly compiles and draws this. Do not create an RT. Pack extras t7–t9 default to `Texture2D`. For a 3D noise volume, `#define ANOMALY_PACK_SRV0_TYPE Texture3D` before the include.

```hlsl
#define ANOMALY_PACK_SRV0_TYPE Texture3D
#include <AnomalyFullscreen.hlsli>

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float intensity = AnomalyPassUniform0.x; // 0–15 on b7; 0–7 unchanged
    float hitT = /* view-space meters along the unjittered ray; 0 = no MV */;
    // Scaled passes (scale 0.5 / 0.25): GBuffer and depth are full-res.
    float z = AnomalyLinearDepth[AnomalyScenePixel(uv)];
    return float4(float3(0.02, 0.05, 0.12) * intensity * (z > 0), hitT);
}
```

Folder default is IsolatedAdd into `LBuffer`. IsolatedMix is **over** (`src + dest * (1 - src.a)`). `src.rgb` must already be **LBuffer energy** — 0–1 albedo with high alpha replaces a bright sky with charcoal. Light with `AnomalySunColor * max(AnomalySunDiffuse, 1)` (extras CB, Slice AI). Night fill is `AnomalyVolumeAmbient()` (Slice AJ) — do not hdr-lift `AnomalySkyLuma`, and do not scale by `AmbientForwardPass` (Keen probe ambient). Night-side / planet-occulted samples call `AnomalySunVisibility(posCamRel, occluderCenterCamRel, occluderRadius, lightWrapMeters)` — pass atmosphere thickness as wrap; the helper **caps wrap at 12% of occluder radius** so geometric AtmosphereRadius cannot sun-light IsolatedMix night. 3-arg overload uses that 12% directly. Keen CSM is camera-local and does not cover a planet disk from orbit. Do not read Keen `frame_.Light` for sun. `IsolatedSub` writes a **0–1 fraction of dest** via `AnomalyIsolatedSub(occ)`; Anomaly blends `dest.rgb * (1 - saturate(src.rgb))` onto dest (same dest RTV as Replace). IsolatedSub does not blit dest for t0. Contact umbra is `(1-minVis) * Intensity`. IsolatedSub umbra sets `temporal: ["InColor","Reactive"]` so DLSS/FRS stamp `.a` — not `ContributeVelocity` (`.a` is not hit distance). IsolatedAdd curtains still use `["InColor","ContributeVelocity"]` and write view-space hit distance (meters) in `SV_Target.a` (0 = no overlay). `Reactive` stamps `reactiveMask` (dilated isolated luma) on the transparent deferred `rc`. AfterAtmosphere cannot sample Keen `DensityLut` (already unbound). Use `ctx.Rc` (Anomaly redirects `MyRender11.RC` during the callback). Cap raymarch steps (`#define MAX 64` plus a runtime `break`). Cheapen with `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)` — do not globally floor SafetyScale, and do not use per-ray `tMin` (grazing warps keep the max). Keep a few steps when far so the volume does not vanish. SafetyScale drops this frame and recovers over ~16 frames; do not hash `frame & 7` into the pixel seed (that strobes when steps drop). `passes[].scale` 0.5 is pixel cost, not a lighting fix. Screen-space pixel radii (SSGI, contact shadows) use `AnomalySceneUvOffset` / `AnomalyInvSceneSize` — `AnomalyPassSize` is the scaled RT and would stretch the march. AfterFullscreen C# that draws that scaled RT must use `FullscreenPassRegistry.DrawFullscreen(rc, width, height)`; Keen `DrawFullscreenQuad()` with no viewport is screen-sized and stores only the top-left of UV 0–1.

HDR AfterLighting can sample Keen tiled point lights without overlaying `LightPoint.hlsl`. `AnomalyFullscreen.hlsli` includes Keen `Frame.hlsli` on HDR slots so `frame_` is declared; Anomaly binds `MyCommon.FrameConstants` on b0. Reconstruct like `LightPoint.hlsl`: `AnomalyLightingUv(pos.xy)` (not interpolator `TEXCOORD` — that ignores `Frame.Screen.offset`), `AnomalyLightingViewPos`, `AnomalyLightingN`. Do not skip `NdotL ≤ 0`. `frame_.Light.maxTileLights` is the **global list stride** (256–4096), not lights per tile; `PrepareLights` fills each tile in race order, so IsolatedSub must evaluate many lights and march the brightest. Bind `historyDepth` / `occupancy` / `pointShadowAtlas` on t7–t9 when the pack requested those products (`RequestOccupancy` / `RequestPointShadows`; default cube cap 4, max 64):

```hlsl
#include <AnomalyFullscreen.hlsli>

uint2 pixel = (uint2)pos.xy;
uint2 tileCoord = pixel % (uint2)max(frame_.Screen.resolution, 1);
tileCoord /= 16;
uint tile = mad(frame_.Light.tiles_x, tileCoord.y, tileCoord.x);
uint n = min(AnomalyTileIndices[tile], frame_.Light.maxTileLights);
AnomalyPointLight light = AnomalyPointLights[AnomalyTileIndices[frame_.Light.tiles_num + mad(frame_.Light.maxTileLights, tile, i)]];
float3 viewPos = AnomalyLightingViewPos(pixel, AnomalyLinearDepth[pixel]);
float3 N = AnomalyLightingN(unpack_normals2(AnomalyGBuffer1[pixel].xy));
float occ = saturate(1.0 - minVis) * saturate(Intensity);
return AnomalyIsolatedSub(occ);
```

## AfterUpscale display (fullscreen)

`Fullscreen/AfterUpscale/Tonemap.hlsl` — t0 is catalog `upscaledColor` after notify, not internal `LBuffer`. Set `temporal: ["InColor","Display"]` so the unique upscaler evaluates HDR.

```hlsl
#include <AnomalyFullscreen.hlsli>

SamplerState PointSamp : register(s0);
SamplerState LinearSamp : register(s1);

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float3 hdr = AnomalySceneColor.SampleLevel(PointSamp, uv, 0).rgb;
    float exposure = exp2(AnomalyAvgLuminance.Load(int3(0, 0, 0)).g);
    float3 bloom = AnomalyBloom.SampleLevel(LinearSamp, uv, 0).xyz;
    // Display-referred grade at ViewportResolution. Do not sample LBuffer here.
    return float4(hdr * exposure + bloom, 1);
}
```

C# display tenants use `ctx.SceneColor` at `ctx.Width` × `ctx.Height` the same way. Anomaly captures t4–t6 on `MyToneMapping.Run`. Keep swapchain / UI composite off this slot. Replace draws into dest when t0 is a different resource (optional `__compute_shader` + `AnomalyComputeDest` u0, compiled with `ANOMALY_FULLSCREEN_COMPUTE`). After DLSS, dest aliases t0 — Anomaly grades into UAV scratch then copies so the upscaler’s `__result` is the graded image. Display-without-upscale stays at `ResolutionI`.

## Overlay Post.Tonemap

Drop `Overlay/Post.Tonemap/Main.hlsl`. Unique basename maps to Keen’s file. If compile fails, that pack rolls back; Keen tonemap returns. Prefer AfterUpscale `Display` when an upscaler is live.

## Includes

System includes use angle brackets so they search Anomaly’s include dir: `#include <Anomaly.hlsli>`. Quoted includes with a Keen base path do not fall through if missing.

`AnomalyFullscreen.hlsli` includes Keen `Common.hlsli` → `Math/Math.hlsli`. That file already defines `float rand(float2)`. Do not redeclare `rand` (or other Keen helpers) in a fullscreen PS — FXC `X3003` disables the program (`id!compile` on Status). Use Keen’s `rand` or a pack-prefixed name.

→ [[Overlay-vs-inject|Mapping rules]] · [[Fullscreen-programs|Fullscreen programs]] · [[GBuffer-attachments|Request a target]]
