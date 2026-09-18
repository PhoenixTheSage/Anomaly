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

- **Implemented:** `CelestialBackgroundRegistry` owns background-only main/probe draws, paired shader compilation, copied live uniforms, immutable float4 catalogue data, view direction/footprint, pre-discard directionDx/directionDy for integrated star profiles, and explicit vanilla fallback. Existing extras stay 320 B.
- **Open, phase 1/3:** in-game hook/scheduling, sky temporal/upscaler and probe-refresh validation; MSAA currently falls back instead of overwriting mixed-coverage pixels. Depth-zero reprojection remains unmodified.
- **Held for phase 4:** sun-specific glare suppression/restoration and partial-disc visibility. Vanilla flare remains in the prototype.
- **Held for phase 5:** projected labels through Anomaly. Corner-text overlays remain unchanged; shader guides must stay out of probes.

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| Swapchain consumers treat `MatchesRenderResolution` as DXGI size | `Velocity` / catalog `Width`/`Height` | UV-sample; `mvUv = mvPx / velSize`. Lighting may `Load` at `svPos.xy`. Do not require `Width == Backbuffer`. A later extras helper can own the conversion. | FrameGen warped output-sized color with internal pixel delta (bolt silhouettes). Camera-from-depth fallback drops object motion. | FrameGen |

## Done (Slice AI / AJ / AK / AL / AM / AN)

| Gap | Where | How | Why pack-private was unsafe | Tenant |
|-----|-------|-----|-----------------------------|---------|
| First-person local mesh map | `PointShadowPass` / `MeshDepth.hlsl` / `LocalCharacter` | MeshDepth VS skins with VertexTemplateBase and interpolates camera-rel world. PS euclidean to LightPos on FaceCb b0. Do not bind Keen DEPTH_ONLY VS. IsolatedSub mins CubeVisibility when RequestPointShadows > 0. Contact still owns GBuffer hits; do not skip TryGetCamRelBox. Extras stay 320 B. Do not Harmony-unhide the FP body. | GBuffer does not contain the walking-FP body. Reconstruct + min blend striped every point light; contact-skip of the suit AABB then removed the only character umbra. Occupancy / AABB / capsule are not a character. | Screen Space Shadows |
| Sun through a volume onto dest | `AnomalyFullscreen.hlsli` / catalog `volumeSunShadow` (Slice AN) | Pack PublishOnly stamps remaining sun (`.r`, `.a=1`). Helper `AnomalyVolumeSunShadow` fail closed to 1 when `.a < 0.5`. AfterAtmosphere IsolatedSub dest with `AnomalyIsolatedSubEnergy`. IsolatedMix after. Extras stay 320 B. Do not bind Keen CSM. | Belly IsolatedMix does not darken terrain. Pack-private RTs and camera-local cascades fight HDR. | Volumetric Clouds |
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


### Contact traversal and atlas quality revision (2026-09-18)

`AnomalyContactShadows.hlsli` exports `AnomalyContactVisibility(depth, receiverView,
geometricNormalView, towardLightView, rayLengthMetres, depthThicknessMetres,
pixelBudget)`. Include AnomalyFullscreen first. It is for current-frame camera
depth only: it uses that frame's projection/reconstruction. Projected pixel
intervals have perspective-correct depth bounds; exhaustion truncates/fades the
ray instead of widening the stride. Receiver tangent-plane rejection and a small
bias replace camera-depth separation gates. Maximum budget is 192 intervals.
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
sampling directions must be rotated from view to world coordinates.
AnomalyPointShadowVisibility uses a deterministic face-clamped 4x4 tent filter
(bilinear visibility convolved with [1,2,1]/4). Shadows reconstructs receivers
at SV_Position pixel centers, matching blocker sampling. GPU regressions pass;
live movement and perspective-switch acceptance remains pending.


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
The fade now uses perspective-correct physical ray fraction (last 10%).

A second, short-range scene still missed hits at a 3 m camera offset after that
fix. Unit-pixel intervals anchored at the ray origin crossed cell boundaries
but sampled just one cell. Traversal now splits at exact X/Y pixel crossings,
so depth overlap is tested within the sampled cell. Subpixel rays receive one
interval rather than being discarded below half a pixel. Budget bounds use a
conservative crossing count; very long projected rays remain budget-limited.

Regression suite now passes 256 receivers at each of ten camera/scale cases:
0/5/15/30/40 m offsets plus a 0.2-scale short-range scene at 0/1/3/6/8 m. It
includes axis-aligned and diagonal rays, lit receivers outside the silhouette,
flat-floor self-hit rejection and finite endpoints. Existing player exclusion
and foreground-wall tests also pass. Off-screen/hidden blockers and unresolved
subpixel geometry remain screen-space limitations. These reproduced failures
are fixed; matching the reported in-game scene still requires visual acceptance.


### Pack sparse volume / SVO catalog — Slice AO (2026-09-18, open)

Volumetric Clouds is moving planet cloud density to a pack-owned sparse voxel
octree (brick leaves) with AfterAtmosphere raymarch + Medium inject. Anomaly
froxels remain camera-relative ≤8 km and must not become the planet store.

Gaps to own in Anomaly (see Docs/Extensibility.md Slice AO):

- StructuredBuffer (or first-class) catalog publish for octree node pools
- `ISharedBuffer` Depth (and format) for 3D brick atlases
- Optional compute bake into catalog UAVs

Clouds workaround: Texture3D depth-1 RGBA32F nodes + R32F brick atlas published
on `clouds.shape` / `clouds.detail` when the octree is active; CPU Immutable bake.
Labeled `// GAP: Slice AO` in pack shaders.


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
