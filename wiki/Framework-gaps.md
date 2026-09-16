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

None. File the next pack-private hole here in the same turn as the workaround.

## Done (Slice AI / AJ)

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| HDR illuminant | `PassExtrasCb.hlsli` / `FrameTemporal` (288 B extras) | `AnomalySunColor`, `AnomalySunDiffuse`, `AnomalySunToward`, `AnomalySkyLuma` from `EnvironmentLight` on b6. Fail closed when Environment is missing. | IsolatedMix with 0–1 RGB over `LBuffer` looks black. `frame_.Light` is Keen layout. | Volumetric Clouds |
| Night / sky illuminant | `PassExtrasCb.hlsli` / `FrameTemporal` (304 B extras) | `AnomalySkyAmbient` = unlifted `SunColorRaw * 0.028`. Helper `AnomalyVolumeAmbient()` caps at the same 3%. Independent of `AnomalySunVisibility` and pack HdrLift. Never `AnomalySkyLuma * HdrLift`. Never `AmbientForwardPass`. | SkyLuma is Rec.709(sun)×AmbientDiffuse. Probe ambient in `AmbientForwardPass` paints IsolatedMix night white. | Volumetric Clouds |
| IsolatedMix energy | docs / extras illuminant | `src.rgb` is **LBuffer energy**. Light with `AnomalySunColor * AnomalySunDiffuse`. No `IsolatedMixLit` (would double-count lit packs). `passes[].scale` is pixel cost. | High `a` + dim `rgb` replaces HDR sky with charcoal. | Volumetric Clouds |
| March LOD | `AnomalyFullscreen.hlsli` | `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)`. Never per-ray `tMin`. Do not floor `AnomalySafetyScale`. | Close-up 48× light-march TDRs. Packs re-invert LOD. | Clouds + Aurora |
| Planet sun occultation | `AnomalyFullscreen.hlsli` | `AnomalySunVisibility(pos, center, occluderRadius, lightWrapMeters)`. Wrap is atmosphere thickness, **capped at 12% of radius**. 3-arg uses 12%. Keen CSM is camera-local. | Hard 150 m sphere umbra cuts inside Keen's twilight limb. Uncapped thickness/r sun-lights IsolatedMix night. | Volumetric Clouds |
| Pack 3D SRVs | `AnomalyFullscreen.hlsli` | `#define ANOMALY_PACK_SRVn_TYPE Texture3D` before the include. | Default `Texture2D` t7–t9 cannot bind a noise volume. | Volumetric Clouds |
| Scaled-pass pixel→UV | `AnomalyFullscreen.hlsli` | `AnomalySceneUvOffset(pixelDelta)` = `pixelDelta * AnomalyInvSceneSize`. GBuffer/depth stay full-res. | `AnomalyInvPassSize` stretches a full-res radius by 1/scale (SSGI Low @1/4 traces 4× too far). | Prism.SSGI |
| Scaled AfterFullscreen quad | `FullscreenPassRegistry.DrawFullscreen` | Pass the RT width/height. Keen `DrawFullscreenQuad()` with no viewport calls `SetScreenViewport()`. | Half/quarter SVGF (or any pack RT) stores only UV 0–0.5 of the full-res mapping; GI lights stretch with quality. | Prism.SSGI |
| AfterLighting LightPoint reconstruct | `AnomalyFullscreen.hlsli` | `AnomalyLightingUv` / `AnomalyLightingViewPos` / `AnomalyLightingN` / `AnomalyScreenUvToTexel`. IsolatedSub writes a 0–1 dest fraction (`AnomalyIsolatedSub`). | Interpolator UV ignores `Screen.offset`. Tile lists are unsorted; `maxTileLights` is the global stride. A 8-light NdotL cull misses the strip and IsolatedSub is ~0. | Screen Space Shadows |
| IsolatedSub dest-write | IsolatedSub compose / AfterLighting dest | Pack writes **0–1 dest fraction**. Anomaly blends `dest*(1-src)` onto dest (Replace dest RTV). IsolatedSub does not blit dest for t0. Reactive stamps `.a`. | destHistory Dest[p] merge never reached Present. Dest-alias blit left mergeCopy bound (identity dest copy). Pack-composited dest does not stack IsolatedSub. | Screen Space Shadows |

Planet center / shell radii stay pack uniforms unless several packs need the same planet snapshot.

## Do not

| Idea | Why not |
|------|---------|
| Harmony on `MyAtmosphereRenderer` / `MyCloudRenderer` for lighting | Lighting belongs on the extras bus. |
| Steal atmosphere t5 (`DensityLut`) after unbind | AfterAtmosphere cannot see Keen’s LUT. |
| Sample Keen shadow cascades from AfterAtmosphere for a planet disk | Cascades are camera-local. Use `AnomalySunVisibility`. |
| Scale `AnomalySkyAmbient` by `AmbientForwardPass` | Keen adds probe ambient into that field. Night IsolatedMix becomes sun-scale. Use `SunColor * 0.028`. |
| Pack-chosen CB slot for sun color | Anomaly allocates b6 extras / b7 uniforms. |
| Per-pack `Frame.hlsli` forks | Keen layout drift breaks every volume at once. |
| `IsolatedMixLit` compose | Packs that already light in sun units would double-count dest. |
| AfterLighting IsolatedSub from interpolator UV / first-N tile lights | LightPoint uses `screen_to_uv(SV_Position)`. Tile lists are unsorted; `maxTileLights` is the global stride. |
| AfterLighting IsolatedSub dest-write via destHistory Dest[p] merge | IsolatedSub blends occupancy onto dest (same dest RTV as Replace). Pack writes `AnomalyIsolatedSub`. |
| AfterLighting dest-alias t0 blit leaving mergeCopy bound | Blit before the pack PixelShader. `DrawIsolated` rebinds `prog.Shader`. |

→ [[Fullscreen-programs]] · [[HLSL-cookbook]] · [[Composition-rules]]
