# Framework gaps

When a pack hits a hole that **the next shader will also hit**, Anomaly should own the API. Packs may ship a labeled workaround so the game runs today. They must not become a second renderer.

Open work lives in [Docs/Extensibility.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Extensibility.md). File new rows here in the same turn as the pack hack.

## How to file

| Field | What to write |
|-------|----------------|
| **Where** | Anomaly type / HLSL include / extras CB field |
| **How** | Append-only extras, new compose, fullscreen helper, catalog name — not a Keen patch |
| **Why** | What breaks if every pack invents it (HDR units, Frame.hlsli layout, TDR step counts) |

## Open

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| Swapchain consumers treat `MatchesRenderResolution` as DXGI size | `Velocity` / catalog `Width`/`Height` | UV-sample; `mvUv = mvPx / velSize`. Lighting may `Load` at `svPos.xy`. Do not require `Width == Backbuffer`. A later extras helper can own the conversion. | FrameGen warped output-sized color with internal pixel delta (bolt silhouettes). Camera-from-depth fallback drops object motion. | FrameGen |

## Done (Slice AI / AJ / AK / AL / AM)

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| First-person local mesh map | `PointShadowPass` / `MeshDepth.hlsl` / `LocalCharacter` | MeshDepth VS skins with VertexTemplateBase and interpolates camera-rel world. PS euclidean to LightPos on FaceCb b0. Do not bind Keen DEPTH_ONLY VS. IsolatedSub mins CubeVisibility when RequestPointShadows > 0. Contact still owns GBuffer hits; do not skip TryGetCamRelBox. Extras stay 320 B. Do not Harmony-unhide the FP body. | GBuffer does not contain the walking-FP body. Reconstruct + min blend striped every point light; contact-skip of the suit AABB then removed the only character umbra. Occupancy / AABB / capsule are not a character. | Screen Space Shadows |
| Planet-night IsolatedMix energy vs dest | `AnomalyFullscreen.hlsli` (Slice AM) | `AnomalyVolumeNight` — AJ × 0.05 at night. `AnomalySunTransmittance` — monotonic squared limb `sqrt(2h/r)` (cap 0.40); OD only after geo is ~1. No `μ≤0` vs grazing-OD split (bright band + hard cut). No sphere-hit `tNear>1`. Extras stay 320 B. | AJ × 1 over dest≈0 is headlights. AJ × 0.30 is a grey deck on a black disk. Raw geo on `μ≤0` + `geo*exp(-OD)` on `μ>0` walls IsolatedMix HDR. | Volumetric Clouds |
| Contact first-hit | AfterLighting march | First solid gather hit then break. Softness is 2×2 gather fraction, not stacked steps. | `vis *= (1-hit)` every step that still sees the same occluder stacked duplicate silhouettes (stairs). | Screen Space Shadows |
| Planet air column / visual atmosphere ceiling | `PassExtrasCb.hlsli` / `PlanetAtmosphere` / `FrameTemporal` (320 B extras) | `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil` — radii from planet center, meters. Fail closed to 0. `AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade`. Do not Harmony `MyAtmosphereRenderer`. | Packs invent `0.90×AtmosphereRadius` or a forced high AGL lift. Optical limb is closer to `AtmosphereAltitude`. Inner-sphere / atmosphere-mesh clip is a limb when the camera rises. | Volumetric Clouds |
| HDR illuminant | `PassExtrasCb.hlsli` / `FrameTemporal` (288 B extras) | `AnomalySunColor`, `AnomalySunDiffuse`, `AnomalySunToward`, `AnomalySkyLuma` from `EnvironmentLight` on b6. Fail closed when Environment is missing. | IsolatedMix with 0–1 RGB over `LBuffer` looks black. `frame_.Light` is Keen layout. | Volumetric Clouds |
| Night / sky illuminant | `PassExtrasCb.hlsli` / `FrameTemporal` (304 B extras) | `AnomalySkyAmbient` = unlifted `SunColorRaw * 0.028`. Helper `AnomalyVolumeAmbient()` caps at the same 3%. Independent of `AnomalySunVisibility` and pack HdrLift. Never `AnomalySkyLuma * HdrLift`. Never `AmbientForwardPass`. | SkyLuma is Rec.709(sun)×AmbientDiffuse. Probe ambient in `AmbientForwardPass` paints IsolatedMix night white. | Volumetric Clouds |
| IsolatedMix energy | docs / extras illuminant | `src.rgb` is **LBuffer energy**. Light with `AnomalySunColor * AnomalySunDiffuse`. No `IsolatedMixLit` (would double-count lit packs). `passes[].scale` is pixel cost. | High `a` + dim `rgb` replaces HDR sky with charcoal. | Volumetric Clouds |
| March LOD | `AnomalyFullscreen.hlsli` | `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)`. Never per-ray `tMin`. Do not floor `AnomalySafetyScale`. | Close-up 48× light-march TDRs. Packs re-invert LOD. | Clouds + Aurora |
| Planet sun occultation | `AnomalyFullscreen.hlsli` | `AnomalySunVisibility(pos, center, occluderRadius, lightWrapMeters)`. Wrap is atmosphere thickness, **capped at 12% of radius**. 3-arg uses 12%. Keen CSM is camera-local. | Hard 150 m sphere umbra cuts inside Keen's twilight limb. Uncapped thickness/r sun-lights IsolatedMix night. | Volumetric Clouds |
| Pack 3D SRVs | `AnomalyFullscreen.hlsli` | `#define ANOMALY_PACK_SRVn_TYPE Texture3D` before the include. | Default `Texture2D` t7–t9 cannot bind a noise volume. | Volumetric Clouds |
| Scaled-pass pixel→UV | `AnomalyFullscreen.hlsli` | `AnomalySceneUvOffset(pixelDelta)` = `pixelDelta * AnomalyInvSceneSize`. GBuffer/depth stay full-res. | `AnomalyInvPassSize` stretches a full-res radius by 1/scale (SSGI Low @1/4 traces 4× too far). | Prism.SSGI |
| Scaled AfterFullscreen quad | `FullscreenPassRegistry.DrawFullscreen` | Pass the RT width/height. Keen `DrawFullscreenQuad()` with no viewport calls `SetScreenViewport()`. | Half/quarter SVGF (or any pack RT) stores only UV 0–0.5 of the full-res mapping; GI lights stretch with quality. | Prism.SSGI |
| AfterLighting LightPoint reconstruct | `AnomalyFullscreen.hlsli` | `AnomalyLightingUv` / `AnomalyLightingViewPos` / `AnomalyLightingN` / `AnomalyScreenUvToTexel`. IsolatedSub writes a 0–1 dest fraction (`AnomalyIsolatedSub`). | Interpolator UV ignores `Screen.offset`. Tile lists are unsorted; `maxTileLights` is the global stride. A 8-light NdotL cull misses the strip and IsolatedSub is ~0. | Screen Space Shadows |
| IsolatedSub dest-write | IsolatedSub compose / AfterLighting dest | Pack writes **0–1 dest fraction**. Anomaly blends `dest*(1-src)` onto dest (Replace dest RTV). IsolatedSub blits dest for t0 when dest aliases LBuffer. `AnomalyIsolatedSubEnergy(removed, dest)`. Reactive stamps `.a`. | destHistory Dest[p] merge never reached Present. Binary minVis IsolatedSub zeros ambient. Pack-composited dest does not stack IsolatedSub. | Screen Space Shadows |
| IsolatedSub energy fraction | `AnomalyFullscreen.hlsli` | `AnomalyIsolatedSubEnergy` / `AnomalyIsolatedSub(float3)`. Clamp `removed` to dest; soft knee. Photometric energy only (no longer-range BRDF tail). `AnomalyIgn` for dither. | `occ = 1-minVis` from any of N strip lights punches dest to black and duplicates hard silhouettes. A tail at the falloff dest-punches the same way (not in the bright center). | Screen Space Shadows |
| Camera-stable contact dither | `AnomalyFullscreen.hlsli` | `AnomalyIgnWorld(world.xz)` unscaled. Pixel `AnomalyIgn` follows the raster. Seed from unjittered view. | Contact / SSGI step phase crawls when the camera moves and the light has not. Seed `* 4` or jittered viewPos.xz flips IGN under Halton. | Screen Space Shadows |
| AfterLighting view → lighting UV | `AnomalyFullscreen.hlsli` | `AnomalyViewToLightingUv` = inverse of live `compute_screen_ray` (BRDF). Contact: `AnomalyLightingViewPosUnjittered` + `AnomalyViewToDepthUv` (unjittered UV + `AnomalyLightingJitterUv`). | `AnomalyUnjitteredViewProj` vs jittered `linearDepth` makes umbras swim. Jittered reconstruct + jittered project crawls the umbra on a static floor (Halton / TAA). | Screen Space Shadows |
| Two json passes, one hlsl | `ShaderPackRegistry.ApplyPassManifest` | Match existing programs by **id**. First json pass for a file adopts the folder-scan id (`pack.filename`); later ids add siblings. | Matching by file overwrote IsolatedSub with debug Replace. Debug off then drew nothing. | Screen Space Shadows |

Planet center / shell radii stay pack uniforms. Air-top / visual ceiling is Slice AK extras. Walking first-person body is Slice AL (`MeshDepth` into `pointShadowAtlas`; IsolatedSub mins CubeVisibility; contact still owns GBuffer hits).

## Do not

| Idea | Why not |
|------|---------|
| Harmony on `MyAtmosphereRenderer` / `MyCloudRenderer` for lighting | Lighting belongs on the extras bus. |
| Skip `Atmosphere_sphere.mwm` / clip IsolatedMix at `AtmosphereRadius` or cloud inner | Proxy is DsvRo. Inner is density. GBuffer linear depth + Slice AK extras. |
| Steal atmosphere t5 (`DensityLut`) after unbind | AfterAtmosphere cannot see Keen’s LUT. |
| Sample Keen shadow cascades from AfterAtmosphere for a planet disk | Cascades are camera-local. Use `AnomalySunTransmittance` (fail closed to `AnomalySunVisibility`). |
| Scale `AnomalySkyAmbient` by `AmbientForwardPass` | Keen adds probe ambient into that field. Night IsolatedMix becomes sun-scale. Use `SunColor * 0.028` for **in-cloud day fill** only. |
| Treat `AnomalyVolumeAmbient` (2.8% sun) as planet-night illuminant | IsolatedMix over dest≈0 is headlights on the night disk. Slice AM `AnomalyVolumeNight` is AJ × 0.05 + monotonic squared-limb transmittance. |
| Return raw `geo` when `μ≤0` and `geo*exp(-OD)` when `μ>0` | Vis peaks on the night side of `μ=0` (bright band) then IsolatedMix HDR walls. Symmetric `smoothstep(-t, t)` then `geo²`. |
| `smoothstep(-t, t*0.45, μ)` / cap twilight at 0.18 | vis=1 ~5° into day while Keen is still twilight; 0.18 sat inside Pertam sunset. Cap 0.40. hdrLift only in deep day. |
| Light IsolatedMix night from AfterAtmosphere dest luma | Night terrain dest≈0 vanishes the volume. Dest is not the grain. |
| Gate planet night with a sphere-hit `tNear > 1` | Grazing disc noise + HashIgn IsolatedMix HDR sun at low alpha: white grain on dest (terrain included). Monotonic squared limb, not a hard `μ≤0` cut. |
| Pack-chosen CB slot for sun color | Anomaly allocates b6 extras / b7 uniforms. |
| Per-pack `Frame.hlsli` forks | Keen layout drift breaks every volume at once. |
| `IsolatedMixLit` compose | Packs that already light in sun units would double-count dest. |
| AfterLighting IsolatedSub from interpolator UV / first-N tile lights | LightPoint uses `screen_to_uv(SV_Position)`. Tile lists are unsorted; `maxTileLights` is the global stride. |
| AfterLighting IsolatedSub clip at photometric `light.range` / 20% lumaFloor / pixel IGN | March past the Keen sphere (~1.4×); IsolatedSub `sum L_i*(1-vis_i)` of the brightest N (photometric energy only); dither with `AnomalyIgnWorld`. |
| AfterLighting IsolatedSub project with `AnomalyUnjitteredViewProj` / scale contact stepLen by `AnomalySafetyScale` / jittered reconstruct for the march | Unjittered view + `AnomalyViewToDepthUv`. SafetyScale dropping 48→8 steps changes bias; umbra jumps when the camera looks. |
| AfterLighting IsolatedSub dest-write via destHistory Dest[p] merge | IsolatedSub blends occupancy onto dest (same dest RTV as Replace). Pack writes `AnomalyIsolatedSubEnergy`. |
| AfterLighting IsolatedSub `occ = 1-minVis` / longer-range BRDF tail / `vis *= (1-hit)` every step | dest-punch at the falloff; stacked silhouettes (stairs). Photometric `AnomalyIsolatedSubEnergy`; first-hit then break. |
| Harmony-unhide first-person body / occupancy 2 m / extras capsule as the suit | GBuffer never contains the FP body. Slice AL mesh map in `pointShadowAtlas`. Occupancy is too coarse. Capsule is a collision proxy. |
| AfterLighting dest-alias t0 blit leaving mergeCopy bound | Blit before the pack PixelShader. `DrawIsolated` rebinds `prog.Shader`. |

→ [[Fullscreen-programs]] · [[HLSL-cookbook]] · [[Composition-rules]]
