# Keen shaders (Space Engineers 1)

Inventory of Keen Software House’s DX11 shader tree, documented for Anomaly’s GBuffer intercept. Source of truth is the game install, not this repo.

**Root:** `<SE install>/Content/Shaders/`  
**This machine:** `T:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Shaders`

**215** `.hlsl` / `.hlsli` files, plus:

- `CacheGenerator.xml` — permutation cookbook for the shader cache
- `D3DX_DXGIFormatConvert.inl` — Microsoft format packing, included from `Common.hlsli`

These are **not** workshop `.sbc` materials. `VRage.Render11` compiles them at runtime (and into a shader cache) as **material × pass × flag** permutations. Entry points are `__vertex_shader` / `__pixel_shader` / `__compute_shader`. `// @define` comments at the top of `.hlsl` files are the permutation knobs.

Paths below are relative to `Content/Shaders/`.

---

## How Keen builds a geometry shader

Each material is three files that **include** the pass, not the other way around:

```
Materials/<Name>/Vertex.hlsl
  → Declarations.hlsli
  → Geometry/VertexTemplateBase.hlsli   // instance matrix, skinning, clip pos
  → vertex_program()                    // material-specific VS
  → Geometry/Passes/VertexStage.hlsli   // dispatches by RENDERING_PASS

Materials/<Name>/Pixel.hlsl
  → Geometry/PixelTemplateBase.hlsli
  → pixel_program()                     // material-specific PS
  → Geometry/Passes/PixelStage.hlsli
```

`Passes/VertexStage.hlsli` / `PixelStage.hlsli` switch on `RENDERING_PASS`:

| ID | Pass | What it writes |
|----|------|----------------|
| 0 | **GBuffer** | 3 MRTs (`SV_Target0–2`), optional `SV_Depth` |
| 1 | **Depth** | Depth/stencil only (`DEPTH_ONLY`); **no color targets** |
| 2 | **Forward** | Near/far lit color (`SV_Target0/1`) for env probes / far objects |
| 3 | **Highlight** | Solid outline color (`b4` Outline CB) |
| 4 | **FoliageStreaming** | Empty PS (geom shader streams instances) |
| 5 | **Transparent** | OIT accum + coverage |
| 6 | **TransparentForDecals** | Writes packed normals if depth matches |
| 100 | **Test** | Debug |

`CacheGenerator.xml` prewarms **Standard, Glass, Holo, Shield, AlphaMasked, TriplanarSingle/Multi/Debris** × **GBuffer, Depth, Forward, Highlight, Transparent, TransparentForDecals**. On disk but not in that list: **AlphaMaskedArray**, **ShieldLit**, **Test**, and the **FoliageStreaming** pass (compiled on demand / special combo).

**Do not inject into Depth.** It must stay 3-attachment-free. Glass/Holo/Shield are marked `UnsupportedPasses="GBuffer"` — they light in the transparent/forward path.

---

## GBuffer layout (Anomaly injection surface)

`GbufferOutput` is **three targets**, no velocity, no object ID:

| Target | Contents |
|--------|----------|
| `SV_Target0` | `rgb` base color, `a` LOD/255 |
| `SV_Target1` | `rg` octahedral view-space normal, `b` AO, `a` unused (or blend alphaN) |
| `SV_Target2` | `r` metal, `g` gloss, `b` emissive, `a` coverage/255 |

`GbufferWrite` / `GbufferWriteBlend` in `GBuffer/GBufferWrite.hlsli` pack that. `GBuffer/GBuffer.hlsli` is the **read** side used by deferred lighting.

Transforms (`VertexTemplateBase.hlsli`):

```hlsl
result.position_clip = mul(result.position_local, view_proj);
```

`view_proj` comes from `projection_.view_proj_matrix` (`Template.hlsli`, CB slot 1). Object matrix is current-only (`object_.matrix_row0–2`, camera-relative). Frame CB (`Frame.hlsli`) has `cameraPositionDelta` but **no previous view-proj**. Skinning is `bone_matrix[60]` in the object CB when `USE_SKINNING` is set.

**Piggyback points for Anomaly** (shared stages; do not fork `Materials/*/Pixel.hlsl`):

- `Geometry/Passes/GBuffer/VertexStage.hlsli` — add velocity interpolant
- `Geometry/Passes/GBuffer/PixelStage.hlsli` + `GBuffer/GBufferWrite.hlsli` — `SV_Target3`

---

## Constant-buffer slots (`Common.hlsli`)

| Slot | Name | Typical contents |
|------|------|------------------|
| b0 | FRAME | `frame_` — view/proj, camera, lights, DRS, fog |
| b1 | PROJECTION | `view_proj_matrix` |
| b2 | OBJECT | 3×4 matrix, color, LOD, optional 60 bones |
| b3 | MATERIAL | per-material (often empty) |
| b4 | FOLIAGE | grass/rock instance table |
| b5 | ALPHAMASK | impostor views |
| b7 | FORWARD | forward projection |

Depth is **reversed** (`COMPLEMENTARY_DEPTH`: foreground is `> 0`, clear is `0`).

SRV slots used by geometry: `BIG_TABLE_*` 10–12, `INSTANCE_INDIRECTION` 13, `INSTANCE_DATA` 14, dither 28, random 29. Lighting reuses some of those numbers on a different pipeline.

---

## Instancing / vertex flags (geometry only)

From `CacheGenerator.xml` and `Template.hlsli`:

| Flag | Meaning |
|------|---------|
| `USE_SIMPLE_INSTANCING` | Stage 2 instance VB (64 B: 3×4 matrix + color) |
| `USE_SIMPLE_INSTANCING_COLORING` | Extra instance color/emissive interpolants |
| `USE_MERGE_INSTANCING` | Absolute world from instance SRV |
| `USE_CUBE_INSTANCING` / `USE_DEFORMED_CUBE_INSTANCING` | Armor/cube blocks; deformed builds tangents in PS |
| `USE_GENERIC_INSTANCING` | Generic instance stream |
| `USE_SKINNING` | 60-bone palette in object CB |
| `USE_VOXEL_DATA` / `USE_VOXEL_MORPHING` | Planet/asteroid voxels |
| `STATIC_DECAL` / `STATIC_DECAL_CUTOUT` | Mesh decals in GBuffer |
| `MQ` / `LQ` | Medium/low quality texture/BRDF cuts |
| `METALNESS_COLORABLE` | Key-color affects metal |
| `USE_TEXTURE_INDICES` | Texture arrays instead of 2D |
| `DEPTH_ONLY` | Depth pass compile |
| `CUSTOM_DEPTH` | PS outputs `SV_Depth` (glass, shields, some cutouts) |
| `MS_SAMPLE_COUNT` | MSAA GBuffer read/write |

Do **not** add `PrevRowMatrix` to Keen’s 64-byte instance VB. Anomaly owns a side SRV.

---

## Anomaly relevance

| Inject / ignore | Why |
|-----------------|-----|
| **Inject GBuffer shared stages only** | Every Standard/AlphaMasked/Triplanar GBuffer permutation includes the same `GBuffer/*Stage.hlsli` + `GBufferWrite.hlsli` |
| **Leave Depth alone** | No extra RT; `DEPTH_ONLY` PS has no targets |
| **Glass / Holo / Shield** | No GBuffer; camera-MV fallback is correct |
| **Foliage / GPU particles / billboards** | Not instance-matrix GBuffer; camera fallback |
| **`Decals.hlsl`** | Writes GBuffer after the main pass; either skip or write camera/parent velocity |
| **No prev matrix in Keen** | History SRV is Anomaly-owned |
| **Skinning** | `bone_matrix[60]` is current-only; previous bones are a later phase |

---

# File catalog

## Root includes (7)

| File | Role |
|------|------|
| `Common.hlsli` | Samplers, CB/SRV slot numbers, reversed-Z helpers, `MAX_SHADOW_CASCADES`, voxel material cap |
| `Frame.hlsli` | `frame_` CB: view/proj/inv, DRS, lights, fog, bloom, `cameraPositionDelta` |
| `Template.hlsli` | Object + projection CBs; `WorldToClip`; merge/cube/generic instancing flags; skinning array |
| `VertexTransformations.hlsli` | Pack/unpack positions, world↔view, screen rays, depth linearize, normal pack |
| `PixelUtils.hlsli` | Dither, hologram dissolve, coverage helpers used by materials |
| `Random.hlsli` | Hash / noise for foliage placement |
| `PSSL.hlsli` | PlayStation shader-language shims (legacy console); unused on Windows DX11 |

## `Stereo/` (1)

| File | Role |
|------|------|
| `StereoStencilMask.hlsl` | Stereo/VR stencil mask pass |

## `Geometry/` — templates (5)

| File | Role |
|------|------|
| `VertexTemplateBase.hlsli` | Builds `VertexShaderInterface`: local→world→clip, skinning, voxel morph, merge/cube/simple instancing. **Current matrix only.** |
| `PixelTemplateBase.hlsli` | `PixelInterface` / `MaterialOutputInterface`; copies object CB color/emissive/LOD |
| `VertexMergeInstancing.hlsli` | Merge-instancing: load `MyPerInstanceData` from SRV (`INSTANCE_DATA` t14) |
| `AlphamaskViews.hlsli` | Billboard/impostor view blending for alpha-masked trees |
| `TriplanarSampling.hlsli` | World-space triplanar sample helper for voxels |

## `Geometry/Passes/` (22)

Shared VS/PS wrappers included by every material.

| File | Role |
|------|------|
| `PassesDefines.hlsli` | Pass enum (table above) |
| `VertexStage.hlsli` / `PixelStage.hlsli` | `#include` the pass folder from `RENDERING_PASS` |
| `GBuffer/VertexStage.hlsli` | Outputs `SV_Position` + material payload + key color / instance coloring. Static decals get a tiny Z bias. |
| `GBuffer/PixelStage.hlsli` | Calls `pixel_program`, then `GbufferWrite` / `GbufferWriteBlend` (decals, `CUSTOM_DEPTH`) |
| `Depth/VertexStage.hlsli` / `PixelStage.hlsli` | Shadow/Z prepass. PS runs `pixel_program` for alphatest/dither only — **no MRT** |
| `Forward/Declarations.hlsli` | Forward CB |
| `Forward/VertexStage.hlsli` | Passes world position |
| `Forward/PixelStage.hlsli` | GGX + CSM + env ambient; writes near/far targets with distance fade |
| `Highlight/VertexStage.hlsli` / `PixelStage.hlsli` | Selection outline; PS is a constant color |
| `Transparent/Declarations.hlsli` | OIT setup |
| `Transparent/VertexStage.hlsli` / `PixelStage.hlsli` | Weighted blended OIT (`accum` + `coverage`) |
| `TransparentForDecals/Declarations.hlsli` | Depth-compare declarations |
| `TransparentForDecals/VertexStage.hlsli` / `PixelStage.hlsli` | Writes packed normals only where scene depth matches (glass receiving decals) |
| `FoliageStreaming/VertexStage.hlsli` / `PixelStage.hlsli` | Empty PS; VS feeds the foliage stream-out path |
| `Test/VertexStage.hlsli` / `PixelStage.hlsli` | Debug pass |

## `Geometry/Materials/` (36)

Shared:

| File | Role |
|------|------|
| `PixelUtilsMaterials.hlsli` | `FeedOutput` / `FeedOutputBuildTangent`: unpack CM/NG/Ext textures into `MaterialOutputInterface`, coloring, metalness |
| `TransparentConstants.hlsli` | Glass/holo/shield color, fresnel, gloss, light multipliers |
| `TriplanarMaterialConstants.hlsli` | Voxel triplanar scale / blend constants |

Each material folder is `Declarations.hlsli` + `Vertex.hlsl` + `Pixel.hlsl`:

| Material | Used for | Notes |
|----------|----------|--------|
| **Standard** | Blocks, characters, most meshes | ColorMetal + NormalGloss + Extensions (+ optional alphamask). Hologram dither. Static-decal cutout. Texture-array variant (`USE_TEXTURE_INDICES`). **Primary GBuffer inject target.** |
| **AlphaMasked** | Trees, foliage cards, cutouts | Alphatest (`clip(alpha-0.5)`), impostor views via `AlphamaskViews`. Flag `ALPHA_MASKED`. No deformed-cube instancing. |
| **AlphaMaskedArray** | Array-textured cutouts | Same idea + `CUSTOM_DEPTH` + view-blend dirs. Not in `CacheGenerator.xml`. |
| **Glass** | Cockpit/window glass | Forward/transparent only (`UnsupportedPasses="GBuffer"`). Fresnel + cubemap specular + `CUSTOM_DEPTH`. |
| **Holo** | Hologram LCD / transparent holo | Same lighting path as glass; extra texture multiplier. No GBuffer. |
| **Shield** | Blocker shields | Refraction (`myRefract`) + rim; transparent. No GBuffer. |
| **ShieldLit** | Lit variant of shield | Adds Phong specular on top of refraction. Not in cache XML. |
| **TriplanarSingle** | Voxel one-material (asteroids/planets) | World-space triplanar, `USE_VOXEL_DATA`. No skinning/decals/most instancing. |
| **TriplanarMulti** | Voxel blend of 3 materials | Weighted triplanar. No Forward in cache. Has a FoliageStreaming combo. |
| **TriplanarDebris** | Voxel debris / floating rocks | Has a real material CB (`triplanarMaterial`). Limited Forward combo (`LQ` only). |
| **Test** | Debug material | Hardcoded 0.5 gray; looks stale (`output.smoothness` vs `gloss`). |

## `GBuffer/` (3)

| File | Role |
|------|------|
| `GBufferWrite.hlsli` | Pack 3 MRTs. **Add `SV_Target3` here.** |
| `GBuffer.hlsli` | Deferred **read**: reconstruct world pos, N, albedo, F0 from the 3 textures + depth + stencil + AO |
| `Surface.hlsli` | `SurfaceInterface` used by lighting |

## `Lighting/` (14) — deferred lighting after GBuffer

| File | Role |
|------|------|
| `LightDir.hlsl` | Fullscreen: sun + IBL ambient + emissive + fog; skybox for background. Reads GBuffer. |
| `LightPoint.hlsl` | Tiled point lights (16×16 tiles, `TileIndices`) |
| `LightSpot.hlsl` | Spotlight volumes + optional cookie (`ReflectorMask`) |
| `PrepareLights.hlsl` | Compute: per-tile light culling into `TileIndices` UAV |
| `Light.hlsli` | Shared lighting entry wrapping GBuffer read |
| `LightDefs.hlsli` | Point/spot structured-buffer layouts |
| `LightingModel.hlsli` | Combines diffuse + specular + sun |
| `Brdf.hlsli` | Metal/rough split: `SurfaceAlbedo` / `SurfaceF0` |
| `DiffuseOrenNayar.hlsli` | Oren–Nayar diffuse |
| `SpecularGGX.hlsli` | GGX specular (main PBR) |
| `SpecularPhong.hlsli` | Phong (shields, some forward) |
| `EnvAmbient.hlsli` | Cubemap IBL diffuse/specular + BRDF LUT |
| `Fog.hlsli` | Distance fog |
| `Utils.hlsli` | Lighting helpers |

## `Shadows/` (4)

| File | Role |
|------|------|
| `Shadows.hlsl` | Compute: cascade shadow map → screen shadow mask (PCF, cascade blend, Poisson) |
| `Csm.hlsli` | Cascade selection, reconstruct world from depth, sample CSM |
| `Shape.hlsl` | Shadow-caster shape / spot shadow projection |
| `ShadowStats.hlsl` | Debug stats for cascades |

## `Postprocess/` (54)

Shared: `Defines.hlsli`, `Postprocess.hlsli`, `PostprocessBase.hlsli` (fullscreen triangle `PostprocessVertex`).

| File / folder | Role |
|---------------|------|
| `Fxaa.hlsl` + `Fxaa3_11.hlsli` | NVIDIA FXAA 3.11 |
| `Blur.hlsl` | Separable blur |
| `EdgeDetection.hlsl` | GBuffer-based edges (uses `gbuffer_edgedetect`) |
| `DepthResolve.hlsl` | MSAA depth resolve |
| `PostprocessCopy.hlsl` | Fullscreen copy |
| `PostprocessCopyFilter.hlsl` | Copy with filter |
| `PostprocessCopyStencil.hlsl` / `PostprocessCopyInverseStencil.hlsl` | Stencil-aware copies |
| `PostprocessClearAlpha.hlsl` | Alpha clear |
| `PostprocessStretch.hlsl` | Stretch blit (DRS / backbuffer) |
| `PostprocessColorizeExportedTexture.hlsl` | Offline/export colorize |
| **HBAO/** (15) | NVIDIA HBAO+: linearize depth, deinterleave, reconstruct normals, coarse AO, blur X/Y, reinterleave. Third-party NVIDIA code with Keen glue. Files: `BlurX.hlsl`, `BlurY.hlsl`, `Blur_Common.hlsli`, `CoarseAO.hlsl`, `ConstantBuffers.hlsli`, `Copy.hlsl`, `DeinterleaveDepth.hlsl`, `DrawNormals.hlsl`, `FetchNormal_Common.hlsli`, `FullScreenTriangle_Common.hlsli`, `LinearizeDepth.hlsl`, `ReconstructNormal.hlsl`, `ReconstructNormal_Common.hlsli`, `ReinterleaveAO.hlsl`, `SharedDefines.hlsli`. |
| **SSAO/** `Ssao.hlsl` | Older SSAO path (quality fallback vs HBAO) |
| **Bloom/** (8) | `Init` → `PreFilter` → `Downscale2/4` → `DownsampleBlur` / `Blur` → `UpsampleBlur`. Also `Defines.hlsli`. |
| **EyeAdaptation/** (7) | Histogram + auto exposure; `ConstantExposure` skip path; debug histogram. Files: `Defines.hlsli`, `ConstantExposure.hlsl`, `UpdateHistogram.hlsl`, `DownSample.hlsl`, `DebugHistogram.hlsl`, `EyeAdaptation.hlsl`, `EyeAdaptation.hlsli`. |
| **LuminanceReduction/** (4) | Parallel reduction: `Init` → `Sum` → `Skip`. Also `Defines.hlsli`. |
| **Tonemapping/** (3) | `Main.hlsl` + `Filters.hlsli` + `Defines.hlsli`; last HDR→LDR step |
| **ChromaticAberration/** `ChromaticAberration.hlsl` | RGB channel offset |

## `Transparent/` (19)

| File | Role |
|------|------|
| `Billboards.hlsl` | World/view-aligned billboards (particles, sprites, LCDs in some paths) |
| `Clouds/Clouds.hlsl` | Volumetric/sprite clouds |
| `ResolveAccumIntoHeatMap.hlsl` | Debug: OIT accum as heatmap |
| **OIT/** `Globals.hlsli`, `Resolve.hlsl` | Weighted blended OIT resolve |
| **Atmosphere/** | Bruneton-style: `AtmosphereCommon.hlsli`, `AtmospherePrecompute.hlsl` (LUT), `AtmosphereGBuffer.hlsl` (aerial perspective into GBuffer/fog), `AtmosphereEnv.hlsl`, `AtmosphereVS.hlsl` |
| **GPUParticles/** | GPU particle system: `Emit.hlsl`, `EmitSkipFix.hlsl`, `Simulation.hlsl` + `Simulation.hlsli` + `SimulationArgs.hlsl`, `Render.hlsl` (quad raster, optional OIT/streaks/lit), `InitDeadList.hlsl`, `Reset.hlsl`, `Globals.hlsli` |

## `Foliage/` (5)

| File | Role |
|------|------|
| `Foliage.hlsl` | Grass/rock cards from streamed instances; `@define ROCK_FOLIAGE` switches include |
| `Foliage.hlsli` | Shared VS/PS structs for streaming |
| `GrassFoliage.hlsli` / `RockFoliage.hlsli` | Placement / wind / scale |
| `FoliageStreaming.hlsl` | Stream-out / generate instances from voxel surfaces |

## `Decals/` (2)

| File | Role |
|------|------|
| `Decals.hlsl` | Deferred decal volumes: project onto GBuffer depth/normals; optional OIT (`RENDER_TO_TRANSPARENT`). Up to 512 decals in one CB. |
| `DecalsCommon.hlsli` | Decal sampling / fade |

Static mesh decals are a **Standard** GBuffer permutation (`STATIC_DECAL` / `STATIC_DECAL_CUTOUT`), not this folder.

## `EnvProbe/` (6)

| File | Role |
|------|------|
| `EnvProbe.hlsl` | Capture / copy cubemap faces (compute) |
| `EnvProbeCopy.hlsl` | Probe copy |
| `EnvProbeBlend.hlsl` | Blend local probes |
| `EnvPrefiltering.hlsl` / `EnvPrefiltering.hlsli` | GGX mip prefilter for IBL |
| `ForwardPostprocess.hlsl` | Post on the forward/probe target |

## `Primitives/` (5)

| File | Role |
|------|------|
| `Primitives.hlsl` | Debug/editor solid primitives |
| `Lines.hlsl` | Line lists (weld preview, gizmos) |
| `Sprites.hlsl` | Screen-space sprites |
| `OcclusionQuery.hlsl` | Hardware occlusion query proxy |
| `GroupOcclusionQuery.hlsl` | Grouped query for merge instancing / distant groups |

## `Math/` (2)

| File | Role |
|------|------|
| `Math.hlsli` | Saturate helpers, reconstruct, octahedral normals, rotate |
| `Color.hlsli` | Color space / luminance |

## `Debug/` (30)

Fullscreen visualizers of GBuffer/lighting (each `Debug*.hlsl` blits one channel):

| File | Role |
|------|------|
| `DebugAlbedo.hlsl` | Albedo (metal-split) |
| `DebugBaseColor.hlsl` | Raw GBuffer0 RGB |
| `DebugNormal.hlsl` / `DebugNormalView.hlsl` | World / view normals |
| `DebugMetalness.hlsl` / `DebugGlossiness.hlsl` | GBuffer2 R/G |
| `DebugAmbientOcclusion.hlsl` | AO |
| `DebugEmissive.hlsl` | Emissive |
| `DebugLOD.hlsl` | LOD from GBuffer0 A |
| `DebugDepth.hlsl` | Linearized depth |
| `DebugNDotL.hlsl` | N·L |
| `DebugAmbientDiffuse.hlsl` / `DebugAmbientSpecular.hlsl` | IBL terms |
| `DebugShadows.hlsl` / `DebugShadowSplits.hlsl` | Shadow mask / cascades |
| `DebugStencil.hlsl` | Stencil |
| `DebugEdge.hlsl` | Edge detect |
| `DisplayHdrIntensity.hlsl` | HDR magnitude |
| `DebugBlitTexture.hlsl` / `DebugBlitTexture3D.hlsl` / `DebugBlitTextureArray.hlsl` / `DebugBlitTextureDepth.hlsl` | Generic texture blit |
| `DebugRt.hlsl` | Render-target dump |
| `Histogram.hlsl` | Luminance histogram |
| `DataVisualization.hlsli` / `DataVisualizationHistogram.hlsl` / `DataVisualizationDebugHistogram.hlsl` | Generic data vis |
| `HeatMap.hlsli` | Heat-map palette |
| `Debug.hlsli` | Shared debug includes |
| `DebugDepthReprojection.hlsl` | Compute **experiment** that reprojects depth with a hardcoded rotation — not production camera MVs |

---

## Counts

| Folder | Files |
|--------|------:|
| Root | 7 |
| Stereo | 1 |
| Geometry (templates + passes + materials) | 63 |
| GBuffer | 3 |
| Lighting | 14 |
| Shadows | 4 |
| Postprocess | 54 |
| Transparent | 19 |
| Foliage | 5 |
| Decals | 2 |
| EnvProbe | 6 |
| Primitives | 5 |
| Math | 2 |
| Debug | 30 |
| **Total `.hlsl`/`.hlsli`** | **215** |
