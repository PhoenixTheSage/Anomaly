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

Post-CopyToRT / swapchain size (frame generation): do **not** `Load` at output pixel. UV-sample the catalog texture and convert `mvUv = mvPx / float2(Width, Height)`. `MatchesRenderResolution` is DRS, not DXGI.

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

Folder default is IsolatedAdd into `LBuffer`. IsolatedMix is **over** (`src + dest * (1 - src.a)`). `src.rgb` must already be **LBuffer energy** — 0–1 albedo with high alpha replaces a bright sky with charcoal. Light with `AnomalySunColor * max(AnomalySunDiffuse, 1)` (extras CB, Slice AI). In-cloud day fill is `AnomalyVolumeAmbient()` (Slice AJ) — do not hdr-lift `AnomalySkyLuma`, and do not scale by `AmbientForwardPass` (Keen probe ambient). That 2.8% is **not** planet-night illuminant: IsolatedMix over dest≈0 is headlights. Planet-night is `AnomalyVolumeNight(albedo, sunVis)` (AJ day fill × `AnomalyVolumeNightScale()` 0.05, Keen night ambient from orbit). Do not use dest luma as an illuminant. Sun vis is `AnomalySunTransmittance(posCamRel, centerCamRel, planetR, airTop)` — monotonic squared limb `sqrt(2h/r)` (cap 0.40). Dest darken from a volume is Slice AN: sample `AnomalyVolumeSunShadow(tex, samp, uv)` (fail closed to 1 when `.a < 0.5`) and IsolatedSub dest with `AnomalyIsolatedSubEnergy`. Do not bind Keen CSM. Do not return raw `geo` when `μ≤0` and `geo*exp(-OD)` when `μ>0` (bright band then HDR wall). Do not `smoothstep(-t, t*0.45)` (vis=1 ~5° into day). A sphere-hit `tNear>1` misses at grazing (HashIgn HDR grain). Daytime 6-step OD only after geo is ~1. Do not use 12% Lambert wrap. Night-side / planet-occulted samples that still need Lambert call `AnomalySunVisibility(posCamRel, occluderCenterCamRel, occluderRadius, lightWrapMeters)` — pass atmosphere thickness as wrap; the helper **caps wrap at 12% of occluder radius** so geometric AtmosphereRadius cannot sun-light IsolatedMix night. 3-arg overload uses that 12% directly. Keen CSM is camera-local and does not cover a planet disk from orbit. Do not read Keen `frame_.Light` for sun. `IsolatedSub` writes a **0–1 fraction of dest** via `AnomalyIsolatedSub` / `AnomalyIsolatedSubEnergy(removed, dest)`; Anomaly blends `dest.rgb * (1 - saturate(src.rgb))` onto dest (same dest RTV as Replace). IsolatedSub blits dest for t0 when dest aliases LBuffer. Do not use `occ = 1-minVis` across many lights — that ORs hard silhouettes and zeros ambient. IsolatedSub umbra sets `temporal: ["InColor","Reactive"]` so DLSS/FRS stamp `.a` — not `ContributeVelocity` (`.a` is not hit distance). IsolatedAdd curtains still use `["InColor","ContributeVelocity"]` and write view-space hit distance (meters) in `SV_Target.a` (0 = no overlay). `Reactive` stamps `reactiveMask` (dilated isolated luma) on the transparent deferred `rc`. AfterAtmosphere cannot sample Keen `DensityLut` (already unbound). Use `ctx.Rc` (Anomaly redirects `MyRender11.RC` during the callback). Cap raymarch steps (`#define MAX 64` plus a runtime `break`). Cheapen with `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)` — do not globally floor SafetyScale, and do not use per-ray `tMin` (grazing warps keep the max). Dither volumes with `AnomalyIgn(pixel)` (do not xor frame index). Contact / SSGI step phase that should not crawl with the camera uses `AnomalyIgnWorld(world.xz)` unscaled — pixel IGN follows the raster; do not `* 4` the seed. Project BRDF with `AnomalyViewToLightingUv` (inverse of live `compute_screen_ray`). Contact reconstructs `AnomalyLightingViewPosUnjittered` and samples depth with `AnomalyViewToDepthUv` (unjittered UV + `AnomalyLightingJitterUv`). `AnomalyUnjitteredViewProj` against jittered `linearDepth`, or a jittered reconstruct for the march, makes umbras swim / Halton-stutter. Do not multiply contact step **count** by `AnomalySafetyScale` — 48→8 during a look changes stepLen/bias and the shadow jumps. `AnomalyMarchSteps` is for volumes. IsolatedSub of tiled lights is `sum L_i * (1-vis_i)` of **photometric** energy (no longer-range BRDF tail — that dest-punches duplicate silhouettes at the falloff, not in the bright center): march the brightest N by energy (not a 20% luma floor / first-N race order, and not one shared vis). March may continue ~1.4× photometric `light.range`; IsolatedSub energy stays inside Keen's sphere. Keen tiles still only list the sphere. Adjacent floor at a grazing camera is 8–15 cm closer in view Z — a 0.08 m sep + 0.5 m thickness IsolatedSub dest to black (hard disc with stair-stepped range). Use ~0.22 m min sep and a fixed along-L origin bias; do not dither origin by stepLen and do not reconstruct a camera-ray radial cone. Keep a few steps when far so the volume does not vanish. SafetyScale drops this frame and recovers over ~16 frames; do not hash `frame & 7` into the pixel seed (that strobes when steps drop). `passes[].scale` 0.5 is pixel cost, not a lighting fix. Screen-space pixel radii (SSGI, contact shadows) use `AnomalySceneUvOffset` / `AnomalyInvSceneSize` — `AnomalyPassSize` is the scaled RT and would stretch the march. AfterFullscreen C# that draws that scaled RT must use `FullscreenPassRegistry.DrawFullscreen(rc, width, height)`; Keen `DrawFullscreenQuad()` with no viewport is screen-sized and stores only the top-left of UV 0–1. AfterAtmosphere IsolatedMix clips volume marches with `AnomalyLinearDepth` (GBuffer voxels/grids) only — do not ray-clip against `AtmosphereRadius` or the cloud inner wall (`Atmosphere_sphere` is DsvRo). Visual ceiling is Slice AK extras (`AnomalyVisualAtmoCeil` / `AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade`) — radii from the planet center, fail closed to 0. Do not invent `0.90 × AtmosphereRadius`. Contact first-hit then break (multiplying vis every step that still sees the same occluder stacks duplicate silhouettes). First-person body is not in GBuffer — Slice AL MeshDepth interpolates camera-rel world into `pointShadowAtlas`; IsolatedSub mins CubeVisibility. Do not skip GBuffer contact inside `TryGetCamRelBox`.

HDR AfterLighting can sample Keen tiled point lights without overlaying `LightPoint.hlsl`. `AnomalyFullscreen.hlsli` includes Keen `Frame.hlsli` on HDR slots so `frame_` is declared; Anomaly binds `MyCommon.FrameConstants` on b0. Reconstruct like `LightPoint.hlsl`: `AnomalyLightingUv(pos.xy)` (not interpolator `TEXCOORD` — that ignores `Frame.Screen.offset`), `AnomalyLightingViewPos`, `AnomalyLightingN`. Do not skip `NdotL ≤ 0`. `frame_.Light.maxTileLights` is the **global list stride** (256–4096), not lights per tile; `PrepareLights` fills each tile in race order, so IsolatedSub must evaluate many lights and march the brightest by energy (top-N, not a 20% luma floor). Photometric `light.range` is the Keen sphere; IsolatedSub may march past it (~1.4×) so the umbra is not a hard disk. Dither contact with `AnomalyIgnWorld` (unscaled, unjittered view), not `AnomalyIgn(pixel)`. Project the march with `AnomalyViewToDepthUv`. First-hit then break (do not multiply vis every step). First-person body is Slice AL MeshDepth (skinned world, no Depth z-clamp). Contact still owns GBuffer hits; IsolatedSub mins CubeVisibility when `RequestPointShadows > 0`. Contact marches `linearDepth` only — Keen `GBuffer0.a` is LOD/255, `GBuffer2.a` is MSAA coverage, not albedo opacity. AlphaMasked `clip()` already punches grate holes into depth; gather 2×2 so a 10 cm hole is not a missed point sample. Bind `historyDepth` / `occupancy` / `pointShadowAtlas` on t7–t9 when the pack requested those products (`RequestOccupancy` / `RequestPointShadows`; default cube cap 4, max 64):

```hlsl
#include <AnomalyFullscreen.hlsli>

uint2 pixel = (uint2)pos.xy;
uint2 tileCoord = pixel % (uint2)max(frame_.Screen.resolution, 1);
tileCoord /= 16;
uint tile = mad(frame_.Light.tiles_x, tileCoord.y, tileCoord.x);
uint n = min(AnomalyTileIndices[tile], frame_.Light.maxTileLights);
AnomalyPointLight light = AnomalyPointLights[AnomalyTileIndices[frame_.Light.tiles_num + mad(frame_.Light.maxTileLights, tile, i)]];
float3 viewPos = AnomalyLightingViewPos(pixel, AnomalyLinearDepth[pixel]);
float3 viewPosMarch = AnomalyLightingViewPosUnjittered(pixel, AnomalyLinearDepth[pixel]);
float3 N = AnomalyLightingN(unpack_normals2(AnomalyGBuffer1[pixel].xy));
return AnomalyIsolatedSubEnergy(removed * saturate(Intensity), AnomalySceneColor[pixel].rgb, unshadowedTotal);
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
