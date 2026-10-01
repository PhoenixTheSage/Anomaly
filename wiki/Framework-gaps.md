# Framework gaps

## SSGI coverage and temporal evidence (2026-10-01, implemented in tenant; Slice AI/AR follow-up)

Prism.SSGI gives accepted coverage counts 6/7 at least one SSILVB angular sector without depth extrusion; thickness still requires >= 8. Coincident front/back horizons must not cancel the accepted illuminant. Full-resolution depth, mip-0 color, sky rejection and world-space radius guards remain required. Positive current-neighbor luminance bounds temporal outliers; dark history and black neighbors cannot establish that new light is a firefly. This is tenant algorithm guidance, with no Anomaly API or extras change.

Prism now releases manager-owned history targets and buffers, invalidates published wrappers before retirement, and rebuilds after device recreation. Fifty production sector-mask cases and the complete temporal shader pass on D3D11 WARP; lifecycle state probes use tracking stand-ins. Live scene acceptance and hardware timing remain pending. See Slice AI/AR in `Docs/Extensibility.md`.

## Checked pack registration (2026-10-01, implemented; Slice AI follow-up)

`ShaderPackRegistry.TryRegister(id, root, requiredFullscreenProgramId, out error)`
reports manifest/scan rejection and checks a required fullscreen program before
queueing it. Legacy `Register` remains compatible. HDR requires `hdr.tonemap`
acceptance before installing render patches; older Anomaly builds leave HDR
inactive with an update message. Acceptance can precede initialization and is
not a compilation/readiness guarantee. See `Docs/ShaderPacks.md`.

## Aurora isolated temporal distance (2026-10-01, implemented; Slice AI follow-up)

D3D11 hardware probes of the current Aurora pixel shader and Anomaly velocity
contributor reproduced these cases: an FP32 reference hit of **89,664.89 m**
becomes **65,504 m** in the framework's FP16 isolated scratch; a black
`(0,0,0,50)` isolated pixel overwrites geometry motion `(3,-2)` with camera-only
`(0,0)`. The current alpha contract carries raw hit metres and implicitly claims
coverage whenever alpha is positive.

`FullscreenPassRegistry.SetVelocityDistanceScale(id, metresPerAlphaUnit)` now
provides an opt-in encode/decode contract: pack alpha is `hitMetres / scale`, and
`IsolatedVelocity.hlsl` multiplies it by the same scale. Default 1 preserves
existing packs. Finite scales in [1, 1,000,000] are accepted before/after program
registration and survive reload/device release; invalid calls return false.
Aurora negotiates 1000 through reflection, otherwise uses 1. Scratch stays FP16;
extras stay 320 B and the contributor reuses constant-buffer padding.

Aurora clears alpha for zero-volume emission and surface-only ambient glow;
their RGB is preserved and geometry motion survives. The host keeps base motion
for invalid/nonpositive decoded distance or invalid history. Positive alpha
still claims coverage: this does not supply a separate distance product for
`IsolatedMix`, whose alpha remains opacity. See `Docs/Extensibility.md`.

Both runtime regression suites and hardware contributor probes pass, with exact
RGB equivalence over 8,294,400 synthetic pixels and translation checks through
1,000,000 m. In-game temporal/device validation and deployment remain pending.

When a pack hits a hole that **the next shader will also hit**, Anomaly should own the API. Packs may ship a labeled workaround so the game runs today. They must not become a second renderer.

Open work lives in [Docs/Extensibility.md](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/Extensibility.md). File new rows here in the same turn as the pack hack.

## How to file

| Field | What to write |
|-------|----------------|
| **Where** | Anomaly type / HLSL include / extras CB field |
| **How** | Append-only extras, new compose, fullscreen helper, catalog name — not a Keen patch |
| **Why** | What breaks if every pack invents it (HDR units, Frame.hlsli layout, TDR step counts) |

## Open

### Point-shadow baseline (2026-09-18)

Corrected shared cube UVs/face-clamped comparison filtering, one character LOD,
and camera-hidden opaque body casters are implemented. The shadow view includes
the head; the main camera remains unchanged. `BufferCatalog.PointShadowStatus`
reports mesh submission counts. Use `AnomalyPointShadows.hlsli` instead of
pack-private cube addressing. Hardware/game animation validation is pending.

Remaining: world-mesh casters with alpha cutouts, stable light identity/selection,
cached world-oriented cube faces and a measured update budget. Keep occupancy and
AABB approximations out of quality validation. The compatibility-named
`AnomalyLightingViewPosUnjittered` now inverts the live raster projection so depth
reconstruction round-trips even with Halton jitter.

### Final Frontier implementation status (2026-09-18)

Sun-glare isolation added 2026-09-18: `SetNativeSunGlare(id, enabled)` controls
only the exact solar light after successful same-frame main replacement; no
world mutation. Defaults on; false supports diagnosing the reported orange
ring. User reports FSR motion-dependent star brightness; wider flux-preserving
profiles are a candidate mitigation, still requiring an in-game comparison.
Analytic effects, partial-disc visibility and full temporal validation remain open.


Initial [celestial support](Celestial-backgrounds.md) is implemented. User validated replacement, live brightness, zoom, sun sizing and day/night tracking; catalogue and remaining compatibility checks are pending. See Slice AI in `Docs/Extensibility.md` and FinalFrontier's `Docs/IntegrationSpike.md`.

- **Implemented:** `CelestialBackgroundRegistry` owns background-only main/probe draws, paired shader compilation, copied live uniforms, immutable float4 catalogue data, optional art Texture2DArray (`SetArt` RGBA8 → t2 + Linear s0; Slice AQ), view direction/footprint, pre-discard directionDx/directionDy for integrated star profiles, and explicit vanilla fallback. Existing extras stay 320 B.
- **Open, phase 1/3:** in-game hook/scheduling, sky temporal/upscaler and probe-refresh validation; MSAA currently falls back instead of overwriting mixed-coverage pixels. Depth-zero reprojection remains unmodified.
- **Held for phase 4:** sun-specific glare suppression/restoration and partial-disc visibility. Vanilla flare remains in the prototype.
- **Held for phase 5:** projected Keen UI labels through Anomaly. Shader name headers and fantasy art overlays are pack-side on the celestial pass (Slice AQ).

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| Swapchain consumers treat `MatchesRenderResolution` as DXGI size | `Velocity` / catalog `Width`/`Height` | UV-sample; `mvUv = mvPx / velSize`. Lighting may `Load` at `svPos.xy`. Do not require `Width == Backbuffer`. A later extras helper can own the conversion. | FrameGen warped output-sized color with internal pixel delta (bolt silhouettes). Camera-from-depth fallback drops object motion. | FrameGen |
| AfterAtmosphere occupancy mip (Slice AS remainder) | catalog mip, not the 64³ camera clipmap | Shared occupancy mip if a second volume cannot reuse weather `.r`. Extras skip scalars + `AnomalyVolumeSkipDt` / `AnomalyVolumeAdvance` are shipped. Do not bake an SDF Texture3D. | Uniform orbit `dt` through empty air is the UHawk “compute 0” tax. A 200 ms static SDF cannot track live wrapping weather and will TDR the test 2080 Ti. | Volumetric Clouds |
| Planet-shell weather UV (Slice AT) | `AnomalyFullscreen.hlsli` helper | Weather: `AnomalyPlanetShellUv` / `AnomalyPlanetSamplePos` (`lon*R*cos(lat)`, no cos floor). Shape tiles: `AnomalyPlanetShapeArcMetres` from the nearer pole. Longitude-chart blend is 0.06 rad at the same point. Extras stay 320 B. Do not use `dot(dir, axis)` or a planet-radius weather tile. | Cartesian Texture3D through the air column is cubic slabs. A `cos(lat)` floor pinwheels the pole. Moving the sample a quarter turn smears the date line. Type LUT `centerH` is a spherical sheet. | Volumetric Clouds |
| Horizon shell continuation (Slice AU) | `AnomalyFullscreen.hlsli` helper | Keep a grazing sample on the shell out to HorizonRange. Extras stay 320 B. Do not lerp a sky sample down onto the deck (pillars). Do not scale the planet or extrude min/max into a slab. Clouds workaround: `CloudFlatDeckPos`. | A few-km deck on a ~60 km planet ends tens of km out. A camera-distance position blend rolls clouds out of the ceiling into that fixed edge, and HorizonRange never moves it. | Volumetric Clouds |

## Done (Slice AI / AJ / AK / AL / AM / AN / AR / AS / AV)

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| Volume ray phase + history snapshot (Slice AV) | `AnomalyFullscreen.hlsli` / `passes[].history` | `AnomalyIgnFrame` is a 16-phase offset of the stable hash. `AnomalyPrevUv` uses `AnomalyPrevViewProj` and fails closed. `passes[].history` blits that program's output into a pack RT after the draw. Extras stay 320 B. Do not xor `AnomalyIgn`. Do not sample catalog `velocity` for a sky volume. Do not put hit distance in IsolatedMix alpha. | A stable ray start is a fixed stipple. Catalog sky velocity is the far plane. `historyColor` includes ships. Each pack inventing a frame xor on `AnomalyIgn` crawls contact shadows and SSGI. | Volumetric Clouds |

## Done (Slice AI / AJ / AK / AL / AM / AN / AR / AS)

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| AfterAtmosphere empty skip | `PassExtrasCb.hlsli` / `AnomalyFullscreen.hlsli` (Slice AS) | `AnomalyVolumeSkipFloor` / `AnomalyVolumeSkipMul` on the 320 B extras tail (was planet pad). Helpers `AnomalyVolumeIsEmpty` / `AnomalyVolumeSkipDt` / `AnomalyVolumeAdvance`. Soft quadratic empty factor (no 6×/1× snap). `maxStep` overload caps empty advance. Fail closed: 0 / ≤1 extras → `0.38` / `6×`; occupancy ≥ floor stays 1×. Occupancy is a pack sample (clouds: weather × deck height). Do not bake a signed-distance Texture3D. | Uniform `dt = chord/steps` through a filled shell spends the budget on empty air. Binary skip + hard lod switches divide the volume and pop on approach. Static SDF bakes TDR. | Volumetric Clouds |
| AfterLighting screen-space gather sky | `AnomalyFullscreen.hlsli` (Slice AR) | `AnomalyIsForeground` plus `AnomalyForegroundCount` (0–9). Illuminants need ≥ 6; thickness needs ≥ 8 (`AnomalyIsSolidForeground` is still 9/9). Complementary-depth 0 reconstructs to ~far. Do not light far-plane / sky LBuffer or isolated sky leaves (count 1–3). 9/9 rejected silhouettes and window edges. Half-res `hiZ` at a sky UV can pull a neighboring foliage depth — lighting uses full-res `linearDepth` at that UV. Gather color is mip 0 only (`litMips` averages canopy holes). World-space `Radius` caps hit distance. Extras stay 320 B. | SSILVB treated sky texels as a huge environment light. Trees facing the planet atmosphere picked up cyan GI. 4-corner mip tests missed canopy holes. | Prism.SSGI |
| First-person local mesh map | `PointShadowPass` / `MeshDepth.hlsl` / `LocalCharacter` | MeshDepth VS skins with VertexTemplateBase and interpolates camera-rel world. PS euclidean to LightPos on FaceCb b0. Do not bind Keen DEPTH_ONLY VS. IsolatedSub mins CubeVisibility when RequestPointShadows > 0. Contact still owns GBuffer hits; do not skip TryGetCamRelBox. Extras stay 320 B. Do not Harmony-unhide the FP body. | GBuffer does not contain the walking-FP body. Reconstruct + min blend striped every point light; contact-skip of the suit AABB then removed the only character umbra. Occupancy / AABB / capsule are not a character. | Screen Space Shadows |
| Sun through a volume onto dest | `AnomalyFullscreen.hlsli` / catalog `volumeSunShadow` (Slice AN) | Pack PublishOnly stamps remaining sun (`.r`, `.a=1`). Helper `AnomalyVolumeSunShadow` fail closed to 1 when `.a < 0.5`. AfterAtmosphere IsolatedSub dest with `AnomalyIsolatedSubEnergy`. IsolatedMix after. Extras stay 320 B. Do not bind Keen CSM. | Belly IsolatedMix does not darken terrain. Pack-private RTs and camera-local cascades fight HDR. | Volumetric Clouds |
| Planet-night IsolatedMix energy vs dest | `AnomalyFullscreen.hlsli` (Slice AM) | `AnomalyVolumeNight` — AJ × 0.02 at night (`AnomalyVolumeNightScale`). `AnomalySunTransmittance` — monotonic squared limb `sqrt(2h/r)` (cap 0.40); OD only after geo is ~1. No `μ≤0` vs grazing-OD split (bright band + hard cut). No sphere-hit `tNear>1`. Extras stay 320 B. | AJ × 1 over dest≈0 is headlights. AJ × 0.30 is a grey deck on a black disk. Raw geo on `μ≤0` + `geo*exp(-OD)` on `μ>0` walls IsolatedMix HDR. | Volumetric Clouds |
| Contact first-hit | AfterLighting march | First solid gather hit then break. Optional `softTaps` 2–8 is a 1D edge filter perpendicular to the light (receiver-centered). 0/1 stays hard. Not stacked `vis *=`. | `vis *= (1-hit)` every step that still sees the same occluder stacked duplicate silhouettes (stairs). A disk around the blocker UV punches the umbra. | Screen Space Shadows |
| Planet air column / visual atmosphere ceiling | `PassExtrasCb.hlsli` / `PlanetAtmosphere` / `FrameTemporal` (320 B extras) | `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil` — radii from planet center, meters. Fail closed to 0. `AnomalyVolumeCeil` / `AnomalyClampRadialToCeil` / `AnomalyVolumeCeilFade`. Do not Harmony `MyAtmosphereRenderer`. | Packs invent `0.90×AtmosphereRadius` or a forced high AGL lift. Optical limb is closer to `AtmosphereAltitude`. Inner-sphere / atmosphere-mesh clip is a limb when the camera rises. | Volumetric Clouds |
| HDR illuminant | `PassExtrasCb.hlsli` / `FrameTemporal` (288 B extras) | `AnomalySunColor`, `AnomalySunDiffuse`, `AnomalySunToward`, `AnomalySkyLuma` from `EnvironmentLight` on b6. Fail closed when Environment is missing. | IsolatedMix with 0–1 RGB over `LBuffer` looks black. `frame_.Light` is Keen layout. | Volumetric Clouds |
| Night / sky illuminant | `PassExtrasCb.hlsli` / `FrameTemporal` (304 B extras) | `AnomalySkyAmbient` = unlifted `SunColorRaw * 0.028`. Helper `AnomalyVolumeAmbient()` caps at the same 3%. Independent of `AnomalySunVisibility` and pack HdrLift. Never `AnomalySkyLuma * HdrLift`. Never `AmbientForwardPass`. | SkyLuma is Rec.709(sun)×AmbientDiffuse. Probe ambient in `AmbientForwardPass` paints IsolatedMix night white. | Volumetric Clouds |
| IsolatedMix energy | docs / extras illuminant | `src.rgb` is **LBuffer energy**. Light with `AnomalySunColor * AnomalySunDiffuse`. No `IsolatedMixLit` (would double-count lit packs). `passes[].scale` is pixel cost. | High `a` + dim `rgb` replaces HDR sky with charcoal. | Volumetric Clouds |
| March LOD | `AnomalyFullscreen.hlsli` | `AnomalyMarchSteps(budget, minSteps, maxSteps, camToVolumeMeters, nearMeters, farMeters)` (slam/TDR). `AnomalyVolumeViewLod(camToVolumeMeters, farMeters)` fades detail 0→1 as the camera leaves the volume. Never per-ray `tMin`. Do not floor `AnomalySafetyScale`. Do not invent a binary orbit flag. | Close-up 48× light-march TDRs. Packs re-invert LOD or snap IsolatedMix to a second orbit look. | Clouds + Aurora |
| Planet sun occultation | `AnomalyFullscreen.hlsli` | `AnomalySunVisibility(pos, center, occluderRadius, lightWrapMeters)`. Wrap is atmosphere thickness, **capped at 12% of radius**. 3-arg uses 12%. Keen CSM is camera-local. | Hard 150 m sphere umbra cuts inside Keen's twilight limb. Uncapped thickness/r sun-lights IsolatedMix night. | Volumetric Clouds |
| Pack 3D SRVs | `AnomalyFullscreen.hlsli` | `#define ANOMALY_PACK_SRVn_TYPE Texture3D` before the include. t12/t13 are SVO extras. | Default `Texture2D` t7–t9 cannot bind a noise volume. | Volumetric Clouds |
| Scaled-pass pixel→UV | `AnomalyFullscreen.hlsli` | `AnomalySceneUvOffset(pixelDelta)` = `pixelDelta * AnomalyInvSceneSize`. GBuffer/depth stay full-res. | `AnomalyInvPassSize` stretches a full-res radius by 1/scale (SSGI Low @1/4 traces 4× too far). | Prism.SSGI |
| Scaled AfterFullscreen quad | `FullscreenPassRegistry.DrawFullscreen` | Pass the RT width/height. Keen `DrawFullscreenQuad()` with no viewport calls `SetScreenViewport()`. | Half/quarter SVGF (or any pack RT) stores only UV 0–0.5 of the full-res mapping; GI lights stretch with quality. | Prism.SSGI |
| AfterLighting LightPoint reconstruct | `AnomalyFullscreen.hlsli` | `AnomalyLightingUv` / `AnomalyLightingViewPos` / `AnomalyLightingN` / `AnomalyScreenUvToTexel`. IsolatedSub writes a 0–1 dest fraction (`AnomalyIsolatedSub`). | Interpolator UV ignores `Screen.offset`. Tile lists are unsorted; `maxTileLights` is the global stride. A 8-light NdotL cull misses the strip and IsolatedSub is ~0. | Screen Space Shadows |
| IsolatedSub dest-write | IsolatedSub compose / AfterLighting dest | Pack writes **0–1 dest fraction**. Anomaly blends `dest*(1-src)` onto dest (Replace dest RTV). IsolatedSub blits dest for t0 when dest aliases LBuffer. `AnomalyIsolatedSubEnergy(removed, dest)`. Reactive stamps `.a`. | destHistory Dest[p] merge never reached Present. Binary minVis IsolatedSub zeros ambient. Pack-composited dest does not stack IsolatedSub. | Screen Space Shadows |
| IsolatedSub energy fraction | `AnomalyFullscreen.hlsli` | `AnomalyIsolatedSubEnergy` / `AnomalyIsolatedSub(float3)`. Clamp `removed` to dest; soft knee. Photometric energy only (no longer-range BRDF tail). `AnomalyIgn` for dither. | `occ = 1-minVis` from any of N strip lights punches dest to black and duplicates hard silhouettes. A tail at the falloff dest-punches the same way (not in the bright center). | Screen Space Shadows |
| Camera-stable contact dither | `AnomalyFullscreen.hlsli` | `AnomalyIgnWorld(world.xz)` unscaled. Pixel `AnomalyIgn` follows the raster. Seed from unjittered view. | Contact / SSGI step phase crawls when the camera moves and the light has not. Seed `* 4` or jittered viewPos.xz flips IGN under Halton. | Screen Space Shadows |
| AfterLighting view → lighting UV | `AnomalyFullscreen.hlsli` | `AnomalyViewToLightingUv` = inverse of live `compute_screen_ray` (BRDF). Contact: `AnomalyLightingViewPosUnjittered` + `AnomalyViewToDepthUv` (unjittered UV + `AnomalyLightingJitterUv`). | `AnomalyUnjitteredViewProj` vs jittered `linearDepth` makes umbras swim. Removing jitter from the inverse alone displaces the receiver. Invert and reapply the same raster projection. | Screen Space Shadows |
| Two json passes, one hlsl | `ShaderPackRegistry.ApplyPassManifest` | Match existing programs by **id**. First json pass for a file adopts the folder-scan id (`pack.filename`); later ids add siblings. | Matching by file overwrote IsolatedSub with debug Replace. Debug off then drew nothing. | Screen Space Shadows |

Planet center / shell radii stay pack uniforms. Air-top / visual ceiling is Slice AK extras. Walking first-person body is Slice AL (`MeshDepth` into `pointShadowAtlas`; IsolatedSub mins CubeVisibility; contact still owns GBuffer hits). Sun through a volume onto dest is Slice AN (`volumeSunShadow` + `AnomalyVolumeSunShadow`).

## Do not

| Idea | Why not |
|------|---------|
| Harmony on `MyAtmosphereRenderer` / `MyCloudRenderer` for lighting | Lighting belongs on the extras bus. |
| Skip `Atmosphere_sphere.mwm` / clip IsolatedMix at `AtmosphereRadius` or cloud inner | Proxy is DsvRo. Inner is density. GBuffer linear depth + Slice AK extras. |
| Steal atmosphere t5 (`DensityLut`) after unbind | AfterAtmosphere cannot see Keen’s LUT. |
| Sample Keen shadow cascades from AfterAtmosphere for a planet disk | Cascades are camera-local. Use `AnomalySunTransmittance` (fail closed to `AnomalySunVisibility`). Dest darken is Slice AN `volumeSunShadow`. |
| Grow extras for volume-sun weights / pack-owned fullscreen shadow RT | Catalog `volumeSunShadow` + helper. Extras stay 320 B. Anomaly owns PublishOnly scratch. |
| Scale `AnomalySkyAmbient` by `AmbientForwardPass` | Keen adds probe ambient into that field. Night IsolatedMix becomes sun-scale. Use `SunColor * 0.028` for **in-cloud day fill** only. |
| Treat `AnomalyVolumeAmbient` (2.8% sun) as planet-night illuminant | IsolatedMix over dest≈0 is headlights on the night disk. Slice AM `AnomalyVolumeNight` is AJ × 0.02 + monotonic squared-limb transmittance. |
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
| Light AfterLighting sky / far-plane LBuffer as GI / environment | Complementary-depth 0 reconstructs to ~far. That texel may still hold last-frame atmosphere. `AnomalyIsForeground` then `AnomalyForegroundCount` ≥ 6 (not 9/9). Gather color is mip 0 only. Do not use half-res `hiZ` as the hit depth at a sky UV. |
| Bake a signed-distance Texture3D from live weather / raise Medium steps to skip empty | Slice AS `AnomalyVolumeSkipDt` + pack occupancy (weather mip). A 200 ms SDF bake TDRs the test 2080 Ti. Catalog `occupancy` is a 64³ camera clipmap, not a planet shell. |

→ [[Fullscreen-programs]] · [[HLSL-cookbook]] · [[Composition-rules]]


### Contact traversal and atlas quality revision (2026-09-18)

`AnomalyContactShadows.hlsli` exports `AnomalyContactVisibility(depth, receiverView,
geometricNormalView, towardLightView, rayLengthMetres, rayThicknessMetres,
pixelBudget)`. Include AnomalyFullscreen first. It is for current-frame camera
depth only: it uses that frame's projection/reconstruction. Projected pixel
intervals pick candidate texels; a hit is Euclidean metres to the 3D light-ray
segment in that pixel (`thickness` plus a texel footprint), not a view-Z slab.
Exhaustion truncates the ray instead of widening the stride. Fade is the last
10 cm of `rayLength`. Behind-camera endpoints are skipped (do not clip onto
the camera plane). Receiver tangent-plane rejection and a small bias replace
camera-depth separation gates. Maximum budget is 192 intervals.
`AnomalyContactVisibilityExcludingPlayer` has an extra `softTaps` argument
(0/1 = hard first-hit; 2–8 = 1D edge filter on neighbor receivers, perpendicular
to the projected light, ~3 cm virtual source, pixel-clamped). Center is the
receiver, not the blocker UV. Invalid / other-surface taps stay occluded so the
umbra interior is not punched. Default wrappers pass 0 so existing packs stay
crisp. This is not `AnomalyPointShadowVisibilitySoft` (cube-face 4×4 tent).
History depth is not supported by this helper; moving previous poses must not
be treated as independent casters. Screen-space visibility is incomplete.

PointShadowPass supports face sizes through 1024. Selection is constrained to
D3D11 dimensions and roughly 96 MiB (RGBA32F plus header): 16 lights at 256,
4 at 512, 1 at 1024. Requested cap and effective selection can differ. Detailed
world-mesh casters, stable light IDs and measured GPU update budgets remain open.
ScreenSpaceShadows is the first tenant. Its atlas visibility no longer depends on
the contact top-N budget. WARP tests cover the shared sampler and contact helper;
Light Test visual/performance acceptance remains pending.


### Camera stability and first-person scope (2026-09-18)

Extra local-character mesh capture is now first-person only. Third person uses
camera-depth contact shadows, with no off-screen third-person caster guarantee.
Atlas faces are world-axis aligned: headers remain view-space light positions;
sampling directions use `AnomalyViewToWorldAt0` (extras InvViewAt0 rows). Do not
`mul(view, frame_.Environment.inv_view_matrix)` — that transpose followed the
camera like a flashlight. Contact `towardLight` is LightPoint view-space (same
axis as the cube ray). Hits are Euclidean metres to that 3D ray segment, not
view-Z overlap. Facing uses GBuffer view N. Do not clip a behind-camera
`finish` onto the camera plane.
AnomalyPointShadowVisibility is a 1-tap compare (default). Soft 4×4 tent is
`AnomalyPointShadowVisibilitySoft` — packs opt in on High/Ultra only. Always
stamp the character mesh on all six cube faces; skipping opposite hemispheres
cuts floor umbras on face-boundary lines. Match atlas rows with
`AnomalyPointShadowMatchRow` (nearest within 5 cm / 2% range) — 1 mm exact
failed until a lucky camera angle. Contact top-N rank by attenuation×NdotL,
not full BRDF. ScreenSpaceShadows quality ceilings face size and atlas light
count so Low is not the same GPU cost as Ultra.
GPU regressions pass; live movement and perspective-switch acceptance remains pending.


### Unified character shadow and light stability (2026-09-18)

This supersedes the first-person-only restriction above: the same skinned atlas
caster runs in both perspectives. Screen-space contact is not a complete
third-person character-shadow substitute.

Atlas header texels 0..63 retain (view-space light position, range). Texel 64
now contains (camera-relative world caster center, valid=1); texel 65 contains
world-axis half extents. Metadata is zero unless mesh draws completed, and is
written after capture. AnomalyContactVisibilityExcludingBounds accepts these
bounds plus the current lighting frame view-to-world rotation. Only a matching
atlas light in Combined mode excludes this caster from depth contact; missing
atlas and ContactOnly retain the original depth path. This is a bounded spatial
approximation: nearby geometry inside the character bounds can also be excluded;
a per-pixel caster identity mask remains the precise long-term solution.

Atlas selection uses the render character center, in-range preference, stable
actor-ID ties and incumbent retention (0.81 squared-distance factor). Status
includes selected actor IDs. Sampling uses the live lighting inverse-view matrix.
GPU tests cover rotated exclusion and preservation of blockers outside bounds.
Live flicker acceptance is still pending; these tests do not reproduce frame
generation, temporal reconstruction or dynamic light culling.


### HDR stage ordering correction (2026-09-18)

The old AfterLighting prefix on MyTransparentRendering.Render ran during
parallel command-list recording. MyRenderScheduler schedules lighting resolve
and transparency independently; CPU light capture could therefore lag the
shadow pass. Worse, linear depth was produced in Scheduler.Done postfix,
after the HDR shadow consumers had used the previous frame's depth.

AfterLighting now runs in MyGBufferResolver.ConsumeWork postfix on the immediate
render context, after lighting submission. OwnedBuffersPass.Execute produces
current depth at this boundary before pack execution. AfterTransparent runs
in MyTransparentRendering.ConsumeWork postfix, after transparency submission.
The late Scheduler.Done depth invocation was removed; velocity diagnostics and
camera-velocity processing remain there. This changes shared HDR-stage timing
for all Anomaly packs and requires live integration validation.

Player contact policy: the mesh atlas serves both perspectives. Header texel
64.w identifies valid player bounds independent of atlas light allocation;
65.w separately indicates successful mesh capture. Contact excludes player
bounds even for lights without an atlas row and in ContactOnly diagnostic mode
(scene-only contact). This prevents partial player silhouettes returning when
coverage changes. It remains a bounding-volume approximation, not a precise
per-pixel actor mask.

TestShadowFrameOrder.ps1 checks the hook topology against the local decompiled
engine schedule and rejects a return to late depth production. GPU contact and
atlas tests remain green. These checks do not establish visual acceptance.


### Player contact mask (2026-09-18)

Combined now excludes the player using a rendered per-pixel depth mask instead
of atlas-header bounds. PointShadowPass renders the same skinned character into
reserved catalog `playerDepth`, an R32_FLOAT texture at render resolution, using
the current camera projection. Values are Euclidean camera distance; clear is
100000. It costs one extra character render and 4 bytes per render pixel.

AnomalyContactVisibilityExcludingPlayer compares scene-point distance with this
mask at each sampled pixel (3 cm minimum tolerance, scaled by depth). It skips
matching player samples while preserving foreground walls. This is raster-depth
matching, not an engine object-ID buffer; discrepancies at silhouettes remain a
live validation concern. Header bounds remain available but no longer drive the
Shadows pack's exclusion. Pack t7 binds playerDepth instead of unused historyDepth.

PlayerMask debug view uses the same predicate: red means excluded player, dark
green means scene contact remains eligible. PointShadowStatus includes mask draw
count. Combined is atlas player plus scene contact; ContactOnly is scene contact
without player; AtlasOnly remains atlas. Synthetic WARP tests cover player removal
and a foreground wall at the same pixels. In-game acceptance remains pending.


### Shared atmospheric volumes (2026-09-18, opt-in acceptance build)

Anomaly now owns provider compilation, coefficient injection, spatial sun optical depth,
independent geometry shadows, room masks, joint integration/reconstruction/composition,
volume history and frame-valid products. Clouds has an atomic near/far adapter; Final
Frontier supplies fog and separate Atmosphere controls. FSR/DLSS receive shared reactive
coverage and FG consumes volume confidence while preserving floating-point color.
113 volume checks and 20 FG color/confidence checks pass. Geometry completeness,
cloud-only appearance, temporal stability and the 6 ms GPU target remain unverified in
live scenes. This is not a shipped extension. See Docs/SharedVolumetrics.md for the draft
contract, exact validation scope and mandatory acceptance checklist.

**2026-09-20:** Joint fog+clouds production compile failed in-game (`CloudOctree`
referenced `AnomalyLinearSampler`, declared only in `AnomalyFullscreen.hlsli`).
`AnomalyVolumeCommon.hlsli` now aliases it to `VolumeLinearClamp`. Sep 18 logs show
the X3004; WARP fog+clouds reproduces the fail before the alias and passes 113 after.

**2026-09-20 (b):** After the sampler fix, enable still failed with
`IndexOutOfRangeException` in `SharedVolumetricRenderer.Bind` —
Keen `MyCommonStage` only has 8 CB slots (0–7); volume shadow constants were on
`b8`. Moved to `b5` in `AnomalyVolumeShadows.hlsli` and the Bind path.


### Camera-distance and cold third-person transform correction (2026-09-18)

Keen MyCullProxy.UpdateWorldMatrix refreshes camera-relative object matrices only
for selected render proxies. The atlas selected a stable highest-detail LOD, so
its cached CommonObjectData.LocalMatrix could be uninitialized on third-person
load or stale after zooming out. This also displaced the player exclusion mask.

PointShadowPass now uploads a private copy of common object constants with the
live actor WorldMatrix minus the current camera in double precision, preserving
Keen's bone-remapping layout. It does not modify the main-view proxy cache. Atlas
LOD remains stable; playerDepth uses CurrentLod to match visible scene geometry.
LOD cross-fade boundaries and unrelated scene-contact distance artifacts remain
live validation concerns. TestCharacterTransform.ps1 compiles the production
transform helper and checks rotated geometry under 0..100 m camera offsets at
million-metre world coordinates.


### Scene-contact camera-distance regression (2026-09-18)

TestContactDistance.ps1 renders a fixed planar blocker/receiver scene on D3D11
WARP while translating the camera. The original shader passed offsets 0/5 m
but weakened at 15 m (visibility 0.412645 instead of <=0.2). Its eight-pixel
end fade covered an increasing fraction of the physical ray with distance.
The fade now uses the last 10 cm of Euclidean distance along the light ray.

A second, short-range scene still missed hits at a 3 m camera offset after that
fix. Unit-pixel intervals anchored at the ray origin crossed cell boundaries
but sampled just one cell. Traversal now splits at exact X/Y pixel crossings,
so Euclidean distance to the light-ray segment is tested at the sampled cell. Subpixel rays receive one
interval rather than being discarded below half a pixel. Budget bounds use a
conservative crossing count; very long projected rays remain budget-limited.

Regression suite now passes 256 receivers at each of ten camera/scale cases:
0/5/15/30/40 m offsets plus a 0.2-scale short-range scene at 0/1/3/6/8 m. It
includes axis-aligned and diagonal rays, lit receivers outside the silhouette,
flat-floor self-hit rejection and finite endpoints. Existing player exclusion
and foreground-wall tests also pass. Off-screen/hidden blockers and unresolved
subpixel geometry remain screen-space limitations. These reproduced failures
are fixed; matching the reported in-game scene still requires visual acceptance.


### Pack sparse volume / SVO catalog — Slice AO (2026-09-20, shipped)

Planet-scale stored density (placed volumes, a simulated brick cache) needs a
pack-owned node pool and brick atlas. Anomaly froxels remain camera-relative
≤8 km and must not become the planet store. Nubis Perlin-Worley is a
procedure; do not bake it into bricks.

Shipped (see Docs/Extensibility.md Slice AO):

- `PublishedBuffer.PublishStructured` for node pools; `Publish3D` for brick
  atlases. `ISharedBuffer.Depth` / `Format`. `Publish` stays the unique 2D
  name so pack `GetMethod("Publish")` is not ambiguous.
- Fullscreen pack extras t12 / t13 (`AnomalyPackSrv3` / `Srv4`) so weather
  stays on t9. t10/t11 stay tiled lights.
- `PublishedBuffer.PublishBake` / `Uav` for a pack compute bake (Anomaly does
  not dispatch).

No current tenant. Volumetric Clouds coarse-skips with the live 64² weather
× height profile. IsolatedMix look is live Nubis RGBA Perlin-Worley +
Worley erosion. Do not steal weather.
Do not bring back a wrapping 128³ weather cube.

### Optical visual ceil vs gameplay air top — Slice AP (2026-09-18, open)

`VisualCeil` equals `AirTop` (`AverageRadius + AtmosphereAltitude`). IsolatedMix
at that radius still sits past Keen's blue Rayleigh rim from orbit. Destination
is a tighter optical `AnomalyVisualAtmoCeil` without changing `AirTop` or extras
size. Do not invent `0.90 × AtmosphereRadius`.

Clouds workaround: Min and Max share one unit, 0 = sea and 1 =
`lerp(hills, visual ceil, AtmosphereFit)`. The gate feathers type decks.
Max > 1 continues past that lid in the same metres. Labeled `// GAP: Slice AP`.

### Planet-shell weather UV — Slice AT (2026-09-21, helper shipped)

Catalog Texture3D wrap is cartesian. Sampling through the air column cuts cubic
slabs; `dot(dir, axis)` weather UV draws small-circle isolines (wavy hill
streaks); a weather tile scaled to planet radius makes 10–30 km pancakes on a
1–2 km deck. Destination is `AnomalyPlanetShellUv` /
`AnomalyPlanetSamplePos` (local east/north metres) plus per-column height from
weather. Extras stay 320 B.

Shipped: `AnomalyPlanetShellMetresFromDir` / `AnomalyPlanetShellMetres` /
`AnomalyPlanetShellUv` / `AnomalyPlanetSamplePos` in `AnomalyFullscreen.hlsli`.
`AnomalyPlanetShellChartWeight` / `AnomalyPlanetShellChartDir` cover the
`atan2` date line on a **longitude** chart: east metres jump by a full turn.
Weight is 0 on −Z and 1 by 0.06 rad. Reproject the yawed direction at the
same point. Do not floor `cos(lat)`.
`AnomalyPlanetShapeArcMetres` / `AnomalyPlanetShapeNorthWeight` are the shape
tile: azimuthal metres from the nearer pole. `sin/cos` of longitude does not
cut the date line, and the pole is a point. Weather stays equirectangular.
Clouds IsolatedMix uses that mapping; weather tiles
are ~16 km × Spacing (Nubis Evolved 2D NDF), map rev 26. The climate map stays
equirectangular and wraps in longitude; its warp is `sin(lon)`, not east metres.
Medium path duplicates the metres math (`// GAP: Slice AT`) because it does not include
`AnomalyFullscreen.hlsli`. Second shell tenant (aurora / dust) still pending.
Do not store WMO type as a global `centerH` sphere.

### Atlas startup diagnostics (2026-09-18)

A third-person startup report showed 14 proxies and 84 atlas draws: mesh
submission was already active. Ultra's RGBA32F atlas budget admits one 1024px
light row out of 46 captured lights despite a requested cap of 10. This does
not establish whether the selected light or mesh raster contents cause the
missing shadow. PointShadowStatus now reports effective/requested capacity and
keeps atlas and playerDepth states separate; the mask can no longer overwrite
the atlas result. Atlas Faces must inspect an allocated row (row 0 in this case).


### Camera-dependent atlas capture (2026-09-18)

Follow-up live evidence rules out a first-person initialization gate: orbiting
the camera in third person changes the raw atlas silhouette violently, while
turning away from the lamp restores its applied shadow. Raw Atlas Faces contains
the character; this is not just a missing draw. Selection remains budget-limited,
but expanding the light budget is not the fix being tested here.

PointShadowPass previously mapped MyCommon.ProjectionConstants for every cube
face and restored the viewer projection. That buffer is also used by Keen's
transparency, occlusion and geometry passes. The atlas/playerDepth renderer now
owns and disposes a separate 64-byte projection CB, binds it at the standard b1
slot, and never maps/restores the engine buffer. Atlas character and optional box
vertices are now relative to each light's world position, subtracted in double
precision; six fixed world-axis cube projections are centered at zero. Thus the
atlas's mesh/projection inputs no longer depend on the viewer. playerDepth keeps
the camera origin and scene projection. Header/sampler coordinates are unchanged.

Validation: both target builds; production transform tests at large world
coordinates and 36 observer positions across all six cube faces; WARP projection,
filter and contact-distance regressions; source integration checks prohibit the
shared projection buffer and enforce the distinct atlas/mask origins. The live
shaking symptom is confirmed; visual acceptance of this correction is pending.
