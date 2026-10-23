# Framework gaps

## Four requested producer gaps — closed (2026-10-16)

The missing Vulkan implementations for **occupancy**, **pointShadowAtlas/playerDepth**, **litMips**, and the shared **froxel/volumeReactive** producer are filled. Their public requests, catalog names/layouts, producer ordering, current-frame validity, fallback/history behavior and device-epoch restoration have targeted GPU and live parity evidence.

- Strict actual-core GPU lane: **273 checks**, validation enabled, zero reported errors; covers pixels, composition/alpha, reactive/motion merging, real Fog/Clouds sources, rotated cameras, resizing and context replacement.
- Actual-core topology lifecycle: **13 checks**; live tests on both backends verify a sealed zero-oxygen room, removed wall/open room, then rebuilt wall/resealed room.
- Matched private DX11 run `a83b4531c5ac4d5883bb4415b68698f7` and final Vulkan run `7d7c38e694614d43a63267a3686fa8ff` pass concurrent producer readiness, interval recovery, fixed-camera off/on capture delivery, the broader simulation fixture and controlled unload/exit. Vulkan retained SDK validation enabled/errors=0, device-lost=0 and no observed managed D3D11 creation attempts. Off/on captures were reviewed.
- Core and companion Release builds pass on net48/net10. No normal game-profile deployment was performed.

This closes the four named missing-producer gaps, not whole-renderer image equality: baseline lighting still differs. Wider adapter/vendor-upscale, actual device-loss fault injection, expanded scene/material and performance acceptance remains separate work. Earlier implementation-stage results below are historical where superseded by this acceptance block. Logs, captures and run manifests are under `SE-Vulkan/TestResults/GuestDepthGpuTests` and `SE-Vulkan/TestResults/CreativeFixture`.

The subsequent lighting/PBR audit is active in `SE-Vulkan/Docs/LightingParity.md`.
The accepted standard is stock features and appearance with documented GPU
tolerances. Controlled static lighting and overlapping OLD/NEW glass now have
reviewed paired images; the latter exposed and corrected a false two-sided
OLD-glass assumption. Published material multipliers/shifts and non-default
transparent gloss mapping are also corrected with GPU checks. Full lighting
feature parity is still unproven. The former 64-local-light cap is now replaced
by stock's 256–4096 point-light quality budgets and a separate 32-spot budget,
with dense GPU/managed-scene checks and an unchanged-scale static comparison.
Stock frustum/group discovery and over-budget selection now have 164 installed
DX11 method comparisons and 35 actual managed-scene GPU checks (validation
zero). These also caught and corrected hidden-parent submission and missing
probe-ambient sphere collection. The discovery build's private static run
`dd9c024bbf4346dbb9b9f0887890e675` completed and exited cleanly, validation zero,
with reviewed DX11-like appearance. That snapshot predates the ambient edit;
the combined ambient/spotlight correction now has 46 scene checks and 121
native-pass GPU checks, validation zero. Canonical static treatment
`f22ab95117e24228a7000aa23b07d069` contains both fixes; reviewed images retain
the small documented HDR residual. The wider material audit found unhandled
`DECAL`/`DECAL_NOPREMULT` mesh-material shader/blend paths, distinct from the
already tested cutout and screen decals. Those blended mesh paths now have
an 80-variant implementation and 684 focused GPU checks (validation zero),
plus passing 534-check WorldPass and 121-check NativePass regressions. Actual
authored scene admission now passes 94 checks, including the installed DX11
DDS header reader. The paired sixteen-panel live fixture admits 8 OLD/8 NEW
actors on stock and 16 blended packets on Vulkan, with matching authored
assets/settings and validation zero. Its reviewed images still fail appearance
parity (largest panel mean RGB8 error about 64/66/66); GBuffer/input diagnosis
is next. Passing standalone blend/submission tests does not close this gap.
Full lighting/material parity remains
open. Sparse scene agreement does not establish every light path. These
lighting changes remain private validation work without normal-profile deployment.

## DX11 pack discovery and catalog uploads (2026-10-14, implemented)

Pulsar sanitizes repository IDs in source-cache directory names. SatelliteLookup now applies the same filename normalization before matching a pack, restoring SSGI's companion discovery while preserving GUID IDs. CPU image uploads now expose Keen ISrvBindable rather than a bare SharpDX view, so fullscreen t7/t8 receive Aurora's noise and ramp. Host-created uploads are retired on replacement/clear/unpublish; borrowed pack resources remain pack-owned. The core forwards lifetime callbacks queued before DX11 commits. Dual-runtime hardware shader readback and source-identity checks pass; in-world SSGI/Aurora visual acceptance remains pending. See Tests/Dx11PackCompatibility.

## Rich HUD host parity and status navigation (2026-10-14, Slice AI, implemented)

The terminal/overlay host previously lived only in Anomaly.Dx11. It now lives in the shared core, so Vulkan queues and mounts the same terminal pages and interactive windows without loading device code. Each runtime plugin has a dedicated Status page, registered independently of Settings/assets/device initialization, and a matching config report. PluginHub is excluded. DX11 uses the live shared registry and retains native settings persistence; Vulkan retains shared settings persistence. Legacy/Interim registration, delayed retry, renderer isolation and Pulsar source identity tests pass. In-world Master interaction/visual acceptance remains pending. See `Docs/TerminalConfig.md` and `Tests/StatusNavigation`.


## Vulkan guest activation and shader inputs (2026-10-16, implemented; live acceptance pending)

Anomaly now compiles in the native-bound frame callback, retries late registrations/renderer availability, prepares driver SPIR-V through SE-Vulkan, and retains compiler/pipeline failures. Generated sources no longer place the whole OS temp tree in the compiler's include snapshot. Manifest binds, priority, slot defines and pack/stock include roots are shared host responsibilities. Native accepts the exact stock 1280-byte frame CB and borrows world light/tile buffers; typed pack storage buffers are accepted at their pack slots. Scene-size publication, linear-depth history, Hi-Z naming/source/half-size, producer ordering and per-frame catalog aliases are corrected. Status distinguishes registration/readiness/submission and does not label a connected frame bus as proven GBuffer writes. `Tests/GuestRuntime` covers real compilation, delayed renderer recovery, catalog routing and optional strict GPU pipelines/pixels. Live game captures remain required for DX11 visual parity.

Requested litMips, occupancy, local-character pointShadowAtlas/playerDepth and the shared froxel/volumeReactive producer have implementations and targeted GPU/live parity evidence summarized above. Wider renderer/adapter acceptance remains separate; pipeline readiness alone is not the acceptance oracle.

Device replacement now has one shared invalidation/restore path. Before guest submission, Anomaly resets native pipelines, ordinary history/storage IDs, occupancy/point-shadow uploads, frame constants and volume history when ResourceEpoch changes. CPU-backed PublishedBuffer textures, volume mip chains and structured floats, plus celestial data/array textures, retain cloned payloads for reupload. Device-end callbacks run before restoration; callback failure blocks submission and retries. Retained GuestRendererImage wrappers expire with their owning device. ResourceEpoch is zero after native context disposal. The hardware recreation fixture checks the actual core across context destruction and replacement; first-frame HDR initialization keeps fallback until the next complete frame. This does not establish loaded-world device-loss recovery or dynamic-resolution lifetime correctness.

## Vulkan guest open-frame resources (2026-10-16, implemented; queued-world/live acceptance pending)

The real Anomaly callback runs between native BeginFrame and EndFrame. Factories previously rejected that phase while offscreen replay passed. Feature 1024 now opens guest pipeline/image allocation after BeginFrame and closes it before EndFrame records the world and guest commands. Image replacement retains previous-submission idle protection; renderer API calls remain mutex-serialized. The normal host refuses older native libraries with an update diagnostic. Native smoke verifies open-frame image creation/upload/replacement. The strict GPU fixture also creates ordinary/extended pipelines, replaces an image and queues a packet in a real menu frame, then checks its replay pixels. This proves the resource phase, not queued-world insertion, loaded-world visuals, recreation or validation-layer acceptance. Native tests pass 15 with one optional skip, and both managed runtimes build.
## Vulkan character point shadows and player depth (2026-10-16, targeted parity verified)


`RequestPointShadows` and `RequestWorldBoxes` retain demand before renderer selection. Anomaly owns light selection, six world-axis cube projections, allocation policy, optional bounded AABB casters and catalog publication. SE-Vulkan supplies copied opaque character triangles, applying the stock four-bone homogeneous skin-position equation on the CPU. The atlas uses a stable available shadow LOD, ignores main-view hiding flags and subtracts the light origin in double precision; player depth uses the current render LOD and camera origin. Only referenced opaque vertices require valid skin data. Missing scene/pose/native support unpublishes the products; spectator geometry produces cleared images and invalid caster headers.

The RGBA32F atlas preserves six horizontal faces, view-space light/range header entries and caster bounds/validity texels 64/65. Face size is 32–1024 with the existing roughly 96 MiB budget and dimension limits. `playerDepth` is render-sized R32F Euclidean depth. Native ABI-1 flags 64/128/256 enable procedural triangle draws, packet viewport/scissor and far clears; MIN blending resolves nearest Euclidean distance without a separate scene depth attachment. Copied packet constants and fence-protected structured buffers retain device ownership in SE-Vulkan.

The strict RTX 5070 Ti GPU lane drives the production managed owner and native replay. Whole-face pixels match independent ray/box distances for six lights, animated skinning and optional boxes; player-depth pixels match the camera oracle. A million-metre observer move leaves atlas geometry bytes identical. Header validity, missing capabilities, spectator clears, resize, homogeneous/four-bone skinning and the actual point-shadow sampling helper are checked. Both managed runtimes build and native tests pass 15 with one optional skip. Validation layers are unavailable. Loaded-world mesh coverage, live DX11 comparisons, recreation and CPU/GPU performance remain pending; the shared froxel/volumeReactive owner is implemented below, with broader acceptance still open.

## Vulkan occupancy producer (2026-10-16, targeted parity verified)

`BufferCatalog.RequestOccupancy` and its fullscreen facade retain the request before renderer selection and work on Vulkan. At AfterLighting, Anomaly reuses the production `OccupancySplat.hlsl` and `OccupancyStamp.hlsl`: clear, splat current linear depth, then stamp bounded actor AABBs. The output is the same 512×512 layout of 64³ camera-relative 2 m cells; Vulkan uses R32F storage for the float UAV (DX11 uses R8). Disabling the request or losing current-frame depth, required pipelines, native features or requested scene inputs removes the alias. A stale depth catalog entry cannot authorize a new occupancy frame.

SE-Vulkan exposes copied render-thread light positions/IDs and actor bounds; Anomaly owns selection and stamps. Point-shadow requests retain the selected-light cap/face size for this policy and the character atlas above. Selection uses the character center, range preference, stable actor-ID ties and incumbent retention. AABBs exclude the local character and the light actor, keep the 80 m extent limit, and retain 48 boxes/light and 256/frame limits. World-minus-camera subtraction stays in double precision. The core tracks local-character state without loading the DX11 renderer.

ABI-1 adds feature-negotiated flags 16 (clear compute target) and 32 (copied packet b0), storage-buffer t0, and reflected 2D compute workgroup sizes. Native guest frame data preserves stock inverse-view rotation. Guest constant uploads now use the physical device alignment independently of world-pipeline initialization: the two-pass GPU probe reproduced missing AABB stamps before this correction and exact recovery after it. Structured uploads retain separate mapped buffers for both fence-protected frame slots and synchronous offscreen replay; updates no longer overwrite in-flight descriptors or wait for the whole device each frame.

`Tests/GuestRuntime` checks request/capability/scene gating, current-depth provenance, dispatch versus atlas dimensions, exclusions, large-coordinate subtraction, selection hysteresis and work caps. RTX 5070 Ti readback compares every atlas cell for rotated/jittered depth, partial workgroups, 65 AABB stamps, changed box uploads and next-frame clearing. Eight owned programs and the real pack pipelines pass; both managed runtimes build. Native tests pass 15 with one optional opaque-reference skip. Validation layers are unavailable. World/device-recreation integration, loaded-scene geometry coverage, visual comparison and GPU/CPU performance remain unverified; the atlas follow-up above supersedes that implementation gap; the shared froxel/volumeReactive owner is implemented below, with broader acceptance still open.

## Vulkan requested lit mips (2026-10-16, targeted parity verified)

Both shared request entry points now retain `RequestLitMips` before renderer selection. Vulkan also detects `litMips` binds on enabled, pipeline-ready programs and the LitMips debug-view selection. The explicit request follows DX11's sticky maximum (2–12 levels, zero clears); automatic demand defaults to five. The physical chain clamps to the scene dimensions. At AfterLighting an Anomaly-owned packet copies current HDR into FP16 and generates each mip before pack consumers. SE-Vulkan owns the copy, linear blits and transfer-to-fragment/compute dependencies on its graphics queue; there is no pack-owned renderer fallback. Linear guest samplers now interpolate between mip levels, while the point sampler retains nearest-mip sampling. Resizing or changing the requested level count recreates the image through the existing idle-protected guest lifetime. Clearing demand removes the catalog alias.

ABI-1 adds packet flag 8 and `sevk_guest_features`; older native libraries leave `litMips` unavailable and report a capability error. The shared request still forwards to DX11 when that renderer commits. `Tests/GuestRuntime` covers explicit and automatic demand, sticky maximum, ordering, small-size clamping, disable and missing-capability recovery. Real Vulkan pixels on RTX 5070 Ti verify FP16 HDR checkerboard averages at LOD 1/2/3 and refreshed base/all levels after source replacement. Anomaly and SE-Vulkan Release net48/net10 builds pass. Validation layers are unavailable on this workstation; live DX11 scene comparison remains pending. Character point shadows/playerDepth are implemented above; the shared froxel/volumeReactive owner is implemented below, with broader acceptance still open.

## Vulkan volumeReactive mask (2026-10-16, targeted producer parity verified)

GuestVolumes now runs the shared light/inject/integrate/reconstruct/reactive chain in the actual Anomaly core and stock renderer. Feature 4096 provides before/after phases, distinct attachment aliases and current-write catalog validity. The strict GPU lane passes 268 checks: analytic scattering/reject pixels, linear HDR scene-times-transmittance-plus-scattering with alpha preservation, host reactive merge, history acceptance, callback cancellation/recovery and stale-product rejection. It also covers the independent scene-depth sources. AfterUpscale replay now precedes the swapchain render scope; catalog IDs no longer collide with extra GBuffer attachments. The fixture uses a controlled medium and injected complete empty-room snapshot. Actual context teardown/replacement now also passes: cloned pack/celestial uploads, expired wrappers, retained demand, fresh volume history and empty occupancy/spectator shadow clears. Live multi-pack/world/topology comparisons, loaded-world device-loss recovery, broader motion/material/resize coverage, performance and validation-layer acceptance remain pending; no deployment or full parity claim is made.

Frame preparation follow-up: shared VolumeFrameData supplies DX11/guest frame, shadow and provider layouts, double-relative provider bytes and guest reversed-projection/history validation. The core now samples the same device-free PlanetAtmosphere source as DX11; its public query no longer fails solely because Vulkan is active. SubmitExtendedBatch accepts the shared five-kernel chain in one native validation/copy operation. Strict GPU checks verify invalid-tail rejection without partial insertion or loss of earlier work, plus the shared struct/kernel pixels. Actual core snapshot/API/clear/reference checks are available through Tests/GuestRuntime --core. The frame owner above now connects scheduling, history, interval commit, composite/motion and current native publication. Loaded-world/pack/topology, device-loss, dynamic-resolution and performance acceptance remain open.

Directional prerequisite update (2026-10-16): Anomaly now requests three independent scene-depth views before frame collection using shared DX11 projection math. SE-Vulkan's separate actor/voxel traversal bypasses main-view and stock-distance culling and resident/GPU ownership skips, while retaining actual depth materials, skin/instance streams and voxel constants. Missing caster geometry or unresolved alpha cancels the request. Copied current-generation source completeness is exposed for the upcoming shared transaction. Strict GPU checks verify production scene collection with installed main-hidden offscreen armor, ownership flags, hidden/empty-frame clears and failure cancellation; The 59-check GPU fixture additionally covers cube/generic streams, nonzero offsets, static groups, changing astronaut skin palettes and an active main-hidden voxel cell. Broader material/deformation variants, actual native residency lifecycle, loaded-world comparisons and recreation/performance remain pending. Shared volume activation, history/commit/composite/motion and current native publication validity are still open.

SE-Vulkan now provides independent depth-view replay under feature 2048. Configure up to three guest D32 targets and copied camera-relative projections before BeginFrame; explicit world-packet mask bits 8–10 select those views. Dedicated projection slots follow the eight stock and 64 spot slots. The regular depth-material variants render the requests before GBuffer/guest consumers, without aliasing stock cascades. Targets clear each requested frame, requests are consumed once, and the recorded-view mask resets at BeginFrame.

Tests/GuestDepthGpuTests passes 23 checks on RTX 5070 Ti using the actual stock world/depth shaders: an offscreen rigid caster has the analytic depth in three independently sized targets alongside eight stock cascades; main pixels stay unchanged; membership, clears, non-shadow exclusion, one-frame validity and duplicate-target rejection pass. Validation layers are unavailable. This is explicit classic world-packet replay. Managed actor/voxel discovery, resident/native-pass/GPU-owned source coverage, other geometry fixtures, live capture and the Anomaly volume transaction remain open.

Directional projection policy now lives in the shared VolumeShadowViews source and the DX11 renderer uses it directly: three 2048² views at 128/1000/8000 m receiver radii, texel-snapped centers, 32 km sunward caster reach and double-relative matrices. Geometry tests cover receiver/far planes, sub-texel stability, large coordinates, the pole basis and offscreen sunward casters. This does not create Vulkan scene-shadow maps or change the remaining producer-integration requirements.

The CPU follow-up now shares VolumeRegistryState between the core guest facade and the DX11 companion. Registration, validation, double origins/bounds, copied parameters, requests, configuration, callbacks and immutable current-frame ownership use one source implementation. Pending state is replayed after DX11 assets initialize; later calls route exclusively to the companion. Vulkan retains the existing unsuppressed guest fallback until the owned frame transaction succeeds. Frame start revokes old intervals and all five volume catalog products, including when bridge reset fails; callback failures revoke readiness while the other callbacks still reset. Status reports this separately from kernel/pipeline readiness.

VolumetricShaderSource and the game-thread airtight-room snapshot now compile from Backend source on both runtimes. VolumeInteriorData owns the common sorted cell layout, generation cache and double-precision camera-relative transforms; DX11 retains its device buffer allocation/upload wrapper. The Vulkan host samples topology on its game update and releases subscriptions on disposal. The frame owner now connects these inputs and products as described above; complete device recreation, live topology/pack comparisons, broader material/motion coverage and performance remain open.

The 2026-10-16 transport follow-up adds feature 512 and a separate 568-byte extended packet, preserving the 416-byte fullscreen packet. It carries copied b0–b7 constants (up to 4096 bytes each), t0–t47, five compute UAVs, reflected XYZ workgroups and two RGBA16F reconstruction targets. Anomaly retains shader composition and producer policy; SE-Vulkan executes packets using its existing frame fences, descriptor pools and uniform allocator. D32 comparison inputs and R16F volumes are supported. Shared kernels explicitly declare SPIR-V storage formats; native checks reflected format/dimension agreement and rejects missing constants/inputs, duplicate outputs and input/output aliases before queuing.

Tests/GuestRuntime compiles the production shared light, injection, integration, reconstruction and coverage shaders. RTX 5070 Ti readback checks partial 9×3×5 workgroups, five outputs, high constant-buffer fields, copied-constant isolation, analytic emission/transmittance/reject coverage, light optical depth and depth comparison. These isolated checks establish the resource/kernel path. The integrated frame owner above now connects provider preparation, directional scene shadows, room buffers, temporal history and interval commit/composite; the legacy guest march remains only as fallback. Loaded-world/pack/topology, device-loss, performance and validation-layer acceptance remain pending.

DX11 frame generation reads catalog `volumeReactive`, the screen reject mask written by the shared froxel renderer. Vulkan now supplies it through GuestVolumes when the complete frame transaction succeeds. Frame generation samples guest image `0xFFFF00000000000B` when a program publishes `volumeReactive`, and otherwise interpolates without a mask. Catalog `velocity` (`0xFFFF00000000000A`) is the live CameraVelocity image and is what the Vulkan warp uses, matching the DX11 convention-15 path. `FrameTemporal.InvalidateHistory` clears the guest history bit so the next warp seeds.

## Interactive RHUD windows (2026-10-02, Slice AI, implemented)

Final Frontier's 3D map needs a resizable interactive viewport rather than the
existing corner-text HudOverlayRegistry. This shared capability now lives in
InteractiveOverlayRegistry API v1: lifecycle/input capture, standard controls,
clipped projected triangle/line batches and virtualized object rows. Payloads are
CLR arrays/delegates to avoid plugin loader tuple identities. Final Frontier owns
entity discovery, permissions/redaction, camera projection and rendering LOD.

Both runtime builds and policy/contract tests pass. A warmed 5,000-object CPU
projection benchmark meets the 2 ms p95 target on this development machine;
total in-world CPU, GPU, Master reset/input capture and visual acceptance are
pending. No GTX 1070 Ti acceptance is claimed. See `Docs/InteractiveOverlays.md`
and Final Frontier `Docs/Map.md` / `Tests/Run-MapTests.ps1`.

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

## Transparent consumer bindings (updated 2026-10-02, implemented; Slice AI follow-up)

The Aurora-enabled thruster billboard report exposed an inherited-state gap.
Keen binds pixel frame b0 and standard samplers once before atmospheres;
billboards rebind vertex b0 but rely on the pixel bindings. Isolated velocity
contribution clears pixel b0 and fullscreen passes replace pixel s0-s2.
Soft particles then read an absent projection matrix for their fade.

`OwnedPassRegistry.Run` now restores the known transparent-stage frame constants
and pixel samplers in an AfterAtmosphere `finally`, through Keen's wrappers on
the supplied rc. This keeps native bindings, the state cache and deferred
recording synchronized, also after a tenant fails. Aurora stays in its original
slot with its shader and temporal policy unchanged. Other tenants share the fix.

32 linked production WARP assertions pass per runtime across immediate/deferred
contexts, including exact consumer pixels after restoration and fault cleanup.
Actual in-game thruster confirmation remains pending. See Slice AI in
`Docs/Extensibility.md` and `Tests/TransparentBindings`.

October 2 Star Dust follow-up: Vanilla and Enhanced are reported invisible even
with a populated pool. DLSS/FSR output-depth preparation clears the context after
the earlier AfterAtmosphere restoration. The host now also restores frame b0,
standard pixel samplers and shadow sampler at the actual
`MyBillboardRenderer.BindResourcesCommon` consumer boundary. This covers native
and upscaler-replacement LDR/PostPP paths without changing render order or depth.
The installed native SOFT_PARTICLE pixel shader reproduces missing-binding
invisibility and exact recovery on synthetic inputs, with soft foreground
clipping retained. The actual built Harmony postfix installs on the installed
game renderer. The expanded runner passes 43 assertions per runtime across
immediate/deferred WARP contexts; full game/upscaler acceptance remains pending.

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

## Shipped

Slice AX shipped object motion and pack GBuffer targets. Catalog `velocity` keeps the RG16F object-motion vector and fills sky, skinned characters, voxels, foliage, and decals from depth. A pack target is `anomaly.json` `"attachments"` or `GBufferTargetRegistry.Request`. Vulkan writes `GBuffer/<name>.hlsl` in a depth-equal pass and publishes sentinel `0xFFFF000000000009` plus the slot. Stock color targets stay three plus depth. Packed `GBuffer1.a` stays a DX11 write into Keen's channel.

Slice AY shipped the bound fullscreen bus. Slice AZ shipped the volume compute packet, the depth-tested celestial packet, glare ownership, and catalog `hdrColor` / `upscaledColor` / `displayColor`. Bilinear compose stays the default until a pack claims the upscale slot. At this initial milestone `pointShadowAtlas` was unavailable; the character point-shadow follow-up above supersedes that limitation.

## Open

### Active display colour mode (2026-10-02)

**Where:** display tenant / `OwnedPassRegistry`, with a structured reflection
contract or append-only view metadata. **How:** declare SDR versus scRGB/HDR
encoding and expose only a ready, enabled display tenant's mode; default to SDR
on absence, failure, conflict and unload. **Why:** `HasDisplayTenant` does not
identify HDR, and an HDR plugin's loaded assembly does not establish activation.
Solar gold looks orange when a palette relying on SDR's channel shoulder passes
through HDR's luminance shoulder. **Tenant:** Final Frontier.

Temporary labeled workaround: query the public status of HDR Output's
`hdr.tonemap` program and use HDR gold only for `live `; poll mode changes without
requiring a settings edit. This depends on a specific tenant and status prefix,
so replace it when the structured capability ships. No Keen lighting patches,
private registry fields or display-format guesses. GPU and bridge transition
checks pass; in-game HDR and glow acceptance remain pending. See Slice AI in
`Docs/Extensibility.md`.

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

Sun-glare worker handoff implemented 2026-10-02: `SetNativeSunGlare(id, enabled)`
controls only the exact solar light, with no world mutation. Keen's transparent
worker can record before the current main celestial draw, so same-frame success
alone failed to suppress overlapping native glare. The host now accepts the
current or previous successful main draw and retains readiness during the next
draw. Main-pass fallback/device release clear readiness; failed, disabled,
conflicting and unregistered providers restore native handling. Older readiness
expires; activation/failure can have an already-recorded transition frame.
The API still defaults to native glare for other providers. FinalFrontier now
claims it for one RHUD-controlled glow and removes its former smooth rim layers.
Twenty-four actual registry/installed-game prefix checks pass on each runtime,
with draw certificates and the clock seeded rather than a running game scheduler.
FinalFrontier passes 87 GPU and 84 integration checks. Partial-disc visibility,
in-game glow and full temporal validation remain open.


Initial [celestial support](Celestial-backgrounds.md) is implemented. User validated replacement, live brightness, zoom, sun sizing and day/night tracking; catalogue and remaining compatibility checks are pending. See Slice AI in `Docs/Extensibility.md` and FinalFrontier's `Docs/IntegrationSpike.md`.

- **Implemented:** `CelestialBackgroundRegistry` owns background-only main/probe draws, paired shader compilation, copied live uniforms, immutable float4 catalogue data, optional art Texture2DArray (`SetArt` RGBA8 → t2 + Linear s0; Slice AQ), view direction/footprint, pre-discard directionDx/directionDy for integrated star profiles, and explicit vanilla fallback. Existing extras stay 320 B.
- **Open, phase 1/3:** in-game hook/scheduling, sky temporal/upscaler and probe-refresh validation; MSAA currently falls back instead of overwriting mixed-coverage pixels. Depth-zero reprojection remains unmodified.
- **Implemented, phase 4:** exact solar glare ownership/restoration with the worker handoff above. **Open:** partial-disc visibility and in-game glare/bloom transition acceptance.
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

## Opaque ray-scene tenant contract (2026-10-03, implemented; Slice AI follow-up)

Updated 2026-10-04: the append-only v1 channel at `frame[14]` supplies physical native-light actor origins separately from Keen's offset positions in field 6. SE-RT defaults to physical origins, exposes an offset opt-in and uses bounded any-hit shadow traversal for detected sources. Nearby off-screen actors were already resident; the engine's explicit visibility flag continues to exclude hidden previews. Unknown coverage cannot certify an unblocked ray, while a known opaque blocker can certify shadow. Native distant sun coverage remains. This baseline was extended by the thin-glass and far-sun contract below; geometry residency and per-pixel ray budgets remain bounded.

Anomaly owns the reusable scene-capture and opaque-HDR ownership boundary for SE-RT. Modern/legacy upload hooks preserve vertices/indices; snapshots include instance transforms, materials, skin poses, voxel material triples, native lamps, billboard/effect proxies, sun shadows and environment probes. Unknown meshes remain explicit coverage. Both owned and fullscreen contributions honor committed ownership, including Prism SSGI and Screen Space Shadows aliases. Completion/reset callbacks keep GPU query polling on the immediate context and tenant resource disposal on renderer lifecycle boundaries.

`Docs/RayTracing.md` documents ABI v1. Native spotlight texture profiles and approximate GPU-effect sources from CPU emitter metadata are connected. Disabled tenants skip frame enumeration while upload capture stays armed. Current source gaps include generic instance-buffer draws, individual GPU-particle extraction, render-target billboard textures and full secondary normal-map/paint/voxel transition shading. Producers can register explicit emissive discs. Native texture-change paths cover screen materials. In-game acceptance and the 1080p midrange GPU budget are still unverified; software WARP checks establish numerical/resource behavior only.

### Shadow quality extension (2026-10-04; implementation with live acceptance pending)

RayScene v1 appends the optional far-only sun product at field 15 and optional
versioned thin-glass optics at material variant field 4. Field 14 physical
origins and every existing field retain their meanings. Tenants explicitly
request far sun through SetFarSunEnabled; disabled tenants incur no far pass.
Anomaly runs one isolated 2048-square directional depth batch, excludes the
256 m resident sphere by fragment clipping and preserves native depth-material
cutouts/dither. Both mesh render paths route depth shader bindings explicitly;
unverified/unready variants invalidate the product and tenants retain native
sun fallback. Owned clipping resources and maps release on world/device lifecycle.

Known GLASS optical data comes from the transparent registry, excluding its
error material and unresolved textures. The descriptor carries unpremultiplied
linear color/opacity, texture/UV, sidedness, category and revision. SE-RT implements
bounded eight-surface RGB transmission, custom 32/4/2-frame histories, finite
emitters and capped sparse full-resolution transport. Shared contracts remain
Anomaly-owned; SE-RT adds no Keen lighting patches. Generic instance buffers,
individual GPU particle geometry and full refractive/media transport remain
coverage/design limits. See Docs/RayTracing.md and SE-RT/Docs/ShadowQuality.md.
WARP and numerical gates do not establish live shader-hook completeness, image
quality or combined GPU performance; those scene/hardware acceptance gates remain.

The native snapshot follow-up separates HDR from tone/upscale scratch images, retains replaced images until the corresponding frame fence (or synchronous replay) completes, and copies HDR using its render dimensions even at AfterUpscale. The 268-check actual-core GPU lane verifies simultaneous distinct HDR/tone reads and render fractions 1, .5, .75, 1 through the stock tone path. This is snapshot/resize coverage; it does not certify the full volume owner's dynamic-resolution history, vendor-upscaler output, loaded-world device loss, real multi-pack/topology or performance acceptance. Native Release tests pass 15 with one optional reference skip; validation layers remain unavailable.
The shared-volume stage follow-up now matches DX11: named volume products bind only after AfterAtmosphere fullscreen work, and the motion overlay runs before BeforeTonemap fullscreen consumers. GPU checks force distinct camera/volume vectors and verify the ordinary consumer sees the overlay. The real FinalFrontier Fog and VolumetricClouds provider sources also run through the actual core transaction with controlled mipmapped noise and the production 64x64x2 weather layout. Fog matches its configured exponential extinction; combined provider extinction matches the sum of separate contributions at every froxel. Missing weather revokes both near intervals and the current reject product, and restoration recovers. CPU-backed image, structured and mip uploads now reuse their native IDs; updating weather bytes removes cloud extinction while preserving fog. These are controlled production-shader tests with injected complete empty-room provenance, not loaded-world or full plugin-adapter acceptance.
Volume-owner resize and validation follow-up: SevkGuestFrame now reports the planned render dimensions in the open-frame callback, using the same logical viewport/fraction policy as EndFrame, and reports current presentation dimensions separately. The earlier presentation-size metadata caused producer allocations to remain oversized under DRS. Fresh sampled guest images now clear deterministically and transition every mip/layer to the advertised shader-read layout before first use; initialization does not mark a catalog product produced. Postprocess images include transfer-source usage for legal HDR/tone snapshot copies. The strict GPU fixture now discovers the repository-private Khronos layer when the caller has not configured one, and checks validation errors after the entire recreated-device lane. Earlier validation_enabled=0 results did not exercise the available private layer.
Current four-feature validation evidence: the strict actual-core lane passes 268 checks with validation_enabled=1 and no Khronos validation errors. It covers 50%, 75% and full render fractions, 95x73 output and 48x37 partial workgroups, automatic old-history rejection followed by new-size recovery, expired handles, and resized playerDepth/litMips/froxel products. It also retains the earlier real Fog/Clouds, callback, recreation, geometry and snapshot checks. Native Release passes all 16 tests with the private layer enabled, including the formerly optional opaque reference. The earlier no-layer results above are historical: the layer is available at SE-Vulkan/Native/build/validation/Bin and the strict GPU runner now discovers it automatically. Loaded-world comparison, full plugin adapters/vendor upscalers, actual device-loss faults and performance remain acceptance work; this does not claim full live DX11 parity.

Loaded-world topology follow-up: the first isolated Creative fixture proved current occupancy/lit mips and 24,949 copied character triangles feeding twelve point-shadow faces/player depth, but failed volume activation. Its EmptyWorld snapshot disabled oxygen and pressurization, leaving physical grids without gas services. The shared snapshot now skips previews/render-only grids, keeps pending physical grids incomplete, bounds all selected physical grids, retains ForcePressurize while tracking ventless topology, starts initial generation when necessary, and restores its prior demand on release. Ten actual-core lifecycle checks cover callback preservation, one-shot generation, invalidation, release and unsupported hooks. These checks do not establish live room correctness. The corrected private fixture copy enables oxygen/pressurization in both Sandbox.sbc and Sandbox_config.sbc before load: the installed loader replaces checkpoint settings with the latter when present. The original snapshot and player settings remain unchanged. Missing gas/topology still cancels volume commitment. Corrected live Vulkan/DX11 captures and real room closure/opening remain required.

Live run 31f275d05c3d4781aacbc2ebbc0852a6 reached 295 shared-volume commits with all requested producers available, then failed after the fixed overview camera. The native bridge had treated QueueWorld's combined view-projection diagonal as projection scale; valid rotated views can make it negative. Guest scale metadata now comes from the stock frame's actual projection matrix. This is a real loaded-camera failure, distinct from the earlier fixture settings failures. The rotated-camera GPU regression and corrected off/on live comparison are still required; the failed run is not full producer-parity acceptance.

The corrected native bridge passes 273 actual-core GPU checks with validation enabled and zero reported errors (`SE-Vulkan/TestResults/GuestDepthGpuTests/volume-projection-validation.log`). This adds the exact rotated overview direction with a negative combined-matrix diagonal, unrotated projection-scale readback, and return-camera recovery to the earlier 268-check coverage. Native Release tests pass with one optional opaque-reference skip in this invocation. Live capture comparison remains open.

Corrected live Vulkan run 241403d2adea45149545b157f21c225c passed the producer-readiness/off-on-delivery gate. All five catalog images were current together; the shared owner resumed its 1000 m interval after re-enable at the fixed overview, with actual engine topology and copied character geometry. Reviewed off/on PNGs show the controlled blue-gray medium adding extinction/emission and the off view restoring the starfield. The run retained validation enabled/errors=0, device-lost=0 and no observed managed D3D11 creation attempts. Its broader simulation fixture later failed the powered-door open/close check; this is not a full fixture pass. Matched DX11 comparison, actual sealed-room closure/opening and performance remain open.

DX11 diagnostic run 9ebdc4973ed74608b1d7f27ba915ce32 exposed two comparison preconditions: EmptyWorld supplied an undefined sun, and dynamic producer requests without a manifest pack did not request their linear-depth dependency. OwnedBuffersPass now honors explicit occupancy demand and requested enabled media; both managed runtimes build. The private fixture sets the same finite sun for both renderers and adds a sealed zero-oxygen room/open-wall/reseal snapshot check. Corrected live verification is pending; do not treat the diagnostic's unavailable occupancy/volumes as a parity comparison.

The next DX11 run (2f3820429cb4490eacfcb2d09ff09eac) verified all products and the sealed zero-oxygen snapshot, but stalled at wall removal. Installed MyGridGasSystem sets a ForcePressurize-only removal flag without recording the removed block, so its incremental removal task leaves the ventless room airtight. Shared tracking now subscribes to grid add/remove/integrity and door state events, immediately invalidates old generation provenance, and coalesces edits into an explicit full generation after any current gas task finishes. Event subscriptions and prior force demand are released with tracking. Thirteen actual-core lifecycle checks and both runtime builds pass; corrected live open/reseal proof remains pending. The stalled run is retained diagnostic evidence.

DX11 run a83b4531c5ac4d5883bb4415b68698f7 now passes actual sealed-zero-oxygen/open-wall/resealed topology agreement, concurrent availability of all five products, fixed-camera off/on capture delivery and interval recovery. Reviewed captures show the controlled medium and clear restoration. The broader simulation fixture also completed and unloaded through its controlled exit. This verifies the dynamic depth dependency and live room refresh on DX11; the matching Vulkan room-refresh run remains in progress.

Matched Vulkan run 7c0c93ed3506463ebac77222c6c31f56 passes the same real room transition, current product gate, interval recovery and off/on capture delivery. SDK validation stayed enabled with zero errors/device loss and the managed observer recorded no D3D11 creation attempts. Both captures were reviewed. The controlled enabled sky region [570,170,620,240] differs by approximately [3.00,3.00,2.00] display RGB levels from DX11; raw means and paths are in SE-Vulkan/TestResults/GuestDepthGpuTests/live-producer-comparison.json. This is a bounded volume-background comparison, not identical whole-image rendering: base scene lighting still differs. The Vulkan broader fixture again failed its door cycle; a wall-clock stall can issue open and close in one update. The harness now waits for an actual fully-open ratio before closing, retaining the original final closed/max-open checks. That harness correction still requires a live rerun. Broader plugin adapters, vendor-upscale/device-loss faults, material coverage and performance acceptance remain open.

Door-sequencing rerun 763298d91a0747d29e713c363c934832 passed the Anomaly producer/topology gates and advanced past the door assertion, then failed missing LCD capture delivery. Its capture log proves overview/LCD text/animation/LCD sprite requests all arrived in the same second after cold compilation. Parity scenarios now cap each update's schedule advance to 100 ms, preserving stage separation while retaining a bounded wall-clock timeout. This is a fixture scheduling fix, not a producer quality change; the canonical live rerun is pending.


Lighting/material follow-up (2026-10-18): the independent SE-Vulkan mesh-decal
fixture exposed an sRGB alphamask-view mismatch and OLD first-use shader macro
aliasing in the installed DX11 cache. The alpha correction now gives exact
packed metal/gloss/emissive equality on all sixteen matched panels; NEW normal
inputs and linear HDR closely match. OLD normal/AO still fails in that
alpha-only live pair. A correction separating authored blend masks from cached
macros passes 1,325 GPU and 113 authored-scene checks with validation zero; its
fresh live comparison is in progress. These results do not reopen the closed
Anomaly occupancy/atlas/lit-mip/shared-volume producer work and do not establish
full lighting/material parity. Overlap ordering, actor distance-fade shader
coverage and the wider final audit remain open. Detailed evidence and private
run identities are in SE-Vulkan/Docs/LightingParity.md.


The mesh-decal cache-corrected live pair is now complete and reviewed. All
sixteen panels agree on packed material channels, depth and screen lighting;
normal/AO differs only at decode precision. Largest panel mean RGB8 differences
are .203/.099/.195 (previously 44.72/54.38/54.54), with P95 at most 2/1/2 codes.
The fixture cleaned up and exited; Vulkan validation and observed managed
D3D11 creation attempts stayed zero. Current regressions: 1,325 focused GPU,
113 authored-scene, 534 world and 121 native-pass checks pass. This closes the
identified alpha-view and OLD cache appearance defects for the fixture.
Overlapping decal order and instanced distance-fade coverage remain part of
the open wider lighting/material audit; full parity is not declared.


The next material audit added actual overlapping red/blue mesh decals. It found
NEW importer part sorting and OLD global DECAL/NOPREMULT technique priority
missing in Vulkan. Both corrections pass 131 authored-scene checks including
same-GPU before/after controls and cross-actor direct/prepared/indirect overlap.
A fresh paired live run matches packed material/depth and normal/AO inputs on
all sixteen panels and was reviewed; largest panel mean RGB8 error is below
1.92, with scoped bright-specular tolerances recorded in the Vulkan journal.
Validation remained zero and cleanup/exit completed. Same-technique resource
ordering, cutout/mixed-model interleaving, instanced distance fade, and the wider
completion audit remain open. An earlier separate world-load buffer-retirement
validation failure is retained as unresolved; subsequent clean runs are not
called its fix. Anomaly producer completion is unchanged.


A further material audit reproduced cutout decals being drawn before blended
mesh decals in Vulkan's fallback path. They now share the late sequence with
stock DECAL, DECAL_NOPREMULT, DECAL_CUTOUT priority. The new zero/full-mask
fixture verifies clipping, global OLD ordering against another blended actor,
exact depth preservation and direct/prepared/indirect agreement. A reviewed
live pair records 32 blended plus 16 cutout packets, matching packed material,
depth and normal/AO inputs; largest panel mean RGB8 error is below 1.15.
Cleanup/exit and validation zero are verified. Current regressions pass 1,325
GPU, 156 scene, 534 world and 121 native-pass checks. This closes the covered
fallback cutout-order defect, not the full material audit: accelerated/mixed
source order, resource ties, raster flags, distance fade and the earlier
unresolved lifetime failure remain tracked in SE-Vulkan/Docs/LightingParity.md.
Anomaly producer completion is unchanged.


The material raster audit now fixes fallback default backface culling,
ALPHA_MASKED_SINGLE_SIDED, OLD actor/material no-cull overrides and the
NEW renderer's fixed technique behavior. OLD depth no-cull also replaces the
stock view bias; NEW depth parts retain their view defaults. The authored
GPU matrix passes 189 checks, including opposing cascades and material-driven
NEW-to-OLD conversion. World/indirect regression passes 536 checks. A reviewed
live pair verifies actual depth on 16 front halves and exactly 4 reversed
halves in both backends; material/depth inputs agree exactly, normal/AO agrees
at decode precision, and largest panel mean RGB8 error is below 1.36. Cleanup,
exit, zero Vulkan validation and zero observed managed D3D11 attempts are
verified. The fixture's detailed HDR/display tolerances and binary identities
are recorded in SE-Vulkan/Docs/LightingParity.md. This closes the measured
fallback culling defect; the wider view/accelerated audit, resource ties,
mixed source ordering, instanced distance fade and unresolved buffer lifetime
failure still prevent full lighting/material parity completion. Anomaly's
previous producer completion is unchanged.


Instanced distance fade now has a source-derived implementation in progress:
246 OLD macro variants and corresponding pass/selection wiring. Actual GPU
scene tests verify generic dither disappearance, stock far-cube position
collapse, unchanged near-cube behavior and unaffected rigid OLD/NEW controls
(20 checks, validation zero). All 1,274 supplemental stages package and the
536-check world regression passes. This remains open until deformed,
intermediate-distance, view/accelerated and paired live evidence is complete;
see SE-Vulkan/Docs/LightingParity.md. No Anomaly producer semantics changed.


Distance-fade coverage has expanded to deformed streams, analytically checked
intermediate dither, stock cascade execution and actual GPU-resident ownership.
The audit fixed retained shader-key invalidation and stale cached cascades
when only the global distance threshold changes; unchanged thresholds still
reuse their maps. It also corrected OLD indexed default culling and a cutout/
DF generic-tint adapter policy that differed from fallback. Current results:
91 focused checks, 103 resident-scene checks, 536 world and 121 native-pass
checks, validation errors zero. Resident visible and intermediate fades match
fallback exactly without managed substitution. The remaining forward/spot/
auxiliary and paired live checks, including stock color semantics for genuine
generic versus optimized rigid sources, keep this item open. Detailed scoped
proof, preserved failures and binary identities are in SE-Vulkan/Docs/LightingParity.md.
Anomaly producer semantics are unchanged; deeper ordering and the existing
lifetime failure are not claimed fixed.


Spotlight fade-cache follow-up now has 120 focused checks: threshold and
camera-distance changes invalidate only affected CPU-submitted spot maps,
while steady inputs reuse them. GPU-only/hybrid spot dependencies remain open.
A separate paired live material audit fixed Vulkan applying legacy entity
DiffuseColor fields that stock DX11 ignores. All 16 OLD/NEW/generic/inactive-DF
panels now match exactly in reviewed RGB8/HDR, with validation errors zero;
46 focused color-contract checks also pass. Actual material tint on genuine
generic instances is a distinct pending audit. Full light/material parity is
still open for ordering/resource ties, remaining accelerated/view/fade coverage,
the earlier lifetime failure and final regressions. Detailed identities and
scope are in SE-Vulkan/Docs/LightingParity.md; Anomaly producers are unchanged.


The genuine-generic material-color audit is now verified separately: DX11 tints
rigid OLD/NEW sources but ignores the multiplier on genuine generic streams.
Vulkan now preserves that distinction, including rigid batching and indexed
resident shaders. Reviewed paired panel RGB8 interiors match exactly; maximum
HDR channel delta is 3.14713e-5. Current results are 82 focused color checks,
112 resident checks, 120 distance-fade checks, 536 world checks and 123
native-pass checks, validation errors zero. The final spot early-return guard
is included in these reruns. Detailed scoped evidence and remaining ordering,
view/accelerated and lifetime gaps remain in SE-Vulkan/Docs/LightingParity.md.
This closes the color audit items, not full parity; Anomaly producer semantics
are unchanged.


The GPU/hybrid spotlight fade-cache reproduction is now fixed and verified.
The resident path previously retained 578 stale depth texels after a fade
threshold change. GPU predicates now track selected DF shader inputs, including
camera-relative translation and the threshold; CPU fade invalidation runs
before their preparation so CPU-only changes also refresh mixed maps. Tests
pass 158 checks in each GPU input mode, plus 120 CPU fade, 536 world and 123
native-pass checks, validation errors zero. Exact depth roundtrips, off-cone
controls, and a CPU-only fading caster beside a fixed GPU caster are covered.
These are scoped generic-stream proofs; remaining forward/auxiliary/live fade,
ordering, accelerated raster and lifetime audit items keep full parity open.
See SE-Vulkan/Docs/LightingParity.md for binary identities and retained failures.
Anomaly producer behavior is unchanged.


Forward/probe frames now receive current render settings; the missing input
previously supplied DistanceFade=0 and removed generic geometry even when the
configured fade should have been inactive. Actual forward captures pass44
focused checks across generic, cube and deformed streams, including partial
analytic dither and far-cube scaling; the full482-check probe regression also
passes with indexed NEW forward coverage. Validation errors are0.
A paired live active-generic-fade capture confirms exact fade/depth coverage
and foreground HDR. Small background-sky differences through fade holes are
quantified under the accepted GPU-tolerance standard. The verifier checks the
raw frame's actual8.5 threshold rather than the stale proxy settings copy.
Dedicated indexed OLD DF forward capture, auxiliary history, ordering,
accelerated raster, lifetime and final-audit items keep full parity open.
Evidence and binary identities are in SE-Vulkan/Docs/LightingParity.md;
Anomaly producer semantics remain unchanged.


Indexed OLD DF forward captures are now verified across generic, cube and
deformed streams:116 focused checks pass in each input mode with exact
fallback radiance in both frame slots. The audit also removed deformed-source
restrictions incorrectly shared with the standalone numerical reference and
enabled the normal scene producer's supported class4 path. The numerical
reference keeps its unsupported-source rejection. Actual MWM/bone-update and
far-fade scene tests pass180 checks in each input mode, including exact buffers,
GPU ownership without managed packets, reentry and cleanup. Broad482 probe,
536 world and123 native-pass regressions pass, validation errors0. Detailed
failures, corrected fixture oracles and final binary identities are recorded
in SE-Vulkan/Docs/LightingParity.md. Auxiliary history, ordering, accelerated
raster, lifetime and final-audit gaps keep full light/material parity active.
Anomaly producer semantics are unchanged.

The asynchronous previous-frame buffer lifetime gap now has a deterministic
instance-growth reproduction and a slot-owned history snapshot fix. The
focused test passes22 checks with an attachment consumer; broader world536
and native-pass123 regressions pass with zero validation errors. Live
world-load verification subsequently passed in private Anomaly run
640fe66a7d844c799963b218ecc3f7e6:7706 submitted frames, zero validation errors,
no device loss, controlled unload/disposal and actual process exit. All five
producer inputs were simultaneously ready. Motion/reactive distance-fade
fidelity, ordering, accelerated raster overrides and the final audit still
keep full parity open. Detailed evidence is in SE-Vulkan/Docs/LightingParity.md.

The CPU-submitted core motion/attachment path now applies stock instanced
distance-fade geometry to current and previous positions using their own
frame thresholds. A negative test found232 surviving pixels missing attachment
data before the correction. The independent projection/readback lane passes284
checks across generic/cube/deformed streams, disabled-fade controls, threshold
restoration and both slots. Final world regression passes536 checks, validation
errors0. GPU-owned/native-pass auxiliary coverage and a shared-buffer offset
audit remain open alongside ordering, accelerated raster and final acceptance.
Binary identities and scoped evidence are in SE-Vulkan/Docs/LightingParity.md.

The shared-buffer auxiliary audit then reproduced a second draw losing its
motion because the binding cache ignored instance offsets (8.53333-pixel
error). The cache now includes vertex/index/instance offsets. Expanded497
focused checks cover independently moving generic/cube/deformed draws and
distinct vertex/index ranges in shared arena pages; the broad536 world checks
also pass, validation errors0. GPU-owned/native-pass auxiliary coverage,
ordering/resource ties, accelerated raster and final acceptance remain open.

Native-pass auxiliary omission is now directly reproduced with a real resident
actor and zero managed world packets. Its main depth was populated while all
attachment pixels and actor motion were absent. Native Colour groups now render
their existing instanced streams into attachments, including the actual
per-instance transform payload. The focused41 attachment checks cover two
actors in one group, both frame slots, complete surviving-pixel coverage and
zero validation errors. CPU-history497 and native-pass123 regressions pass.
Native-pass motion still fails its independent3.2px expectation, and indexed
GPU auxiliary coverage remains open. Detailed negative/positive evidence and
binary identities are recorded in SE-Vulkan/Docs/LightingParity.md.

Native-pass actor motion is now implemented with generational actor history
matched independently of material/stream order and copied into the current
fenced slot. The strict156-check lane covers movement, camera movement,
appearance changes, same-owner recreation, frame gaps, attachment payloads and
asynchronous growth to4096 actors in one instanced group. The gap test also
fixed a menu shadow-target dimension mismatch that had caused validation
errors and device loss. Final validation errors0; CPU-history497,
native-pass123 and world536 regressions pass. Indexed GPU auxiliary coverage,
ordering/resource ties, accelerated raster and final lifecycle/whole-scope
acceptance remain open. Detailed diagnostics and identities are in the parity
journal; ordinary native assets were not deployed.

Indexed GPU attachment replay now uses the actual GPU-generated records and
indirect argument/count ranges, preserving GPU ownership with zero managed
draw packets. The negative control had400 main depth pixels and no attachment
data. The final262 checks pass in both input modes across NEW and OLD
rigid/generic/cube/deformed sources, two attachments, both slots and active
far DF; every surviving pixel has correct translation/basis payloads. Broad180
GPU-scene,497 CPU-history and156 native-pass history checks also pass with
validation errors0. The shared auxiliary geometry helper and native binding
contracts are documented in SE-Vulkan/Docs/LightingParity.md. Indexed GPU
motion, read-only decal/mixed ordering coverage, resource ties, accelerated
raster and final whole-scope/lifecycle acceptance remain open.

Indexed GPU motion now reuses the real indirect ranges and stock geometry,
with generational actor history and immutable prior source snapshots owned
by the current fenced slot. The negative control missed an independently
expected3.995px movement. Final429 checks pass in both GPU input modes with
zero validation errors, including actor/source/deformation/DF/camera/FOV
motion, frame-gap resets and asynchronous source/actor growth to4096 entries.
FOV checks document RG16F precision rather than require sub-step accuracy.
CPU-history497 and native-pass-history156 regressions also pass. Details,
candidate hashes and diagnostic evidence are in SE-Vulkan/Docs/LightingParity.md.
Read-only decal/mixed-source ordering, resource ties, accelerated raster and
the final whole-scene lifecycle/live audit keep full parity open; ordinary
native assets remain unchanged.

The mixed-owner main-pass audit reproduced an OLD read-only cutout overwriting
a later NEW solid: Vulkan returned red where stock submission order requires
blue, with identical depth. Managed, indexed and native-pass draws now share
the OLD/NEW submission phases, and indirect grouping preserves that boundary.
The focused141 checks pass in both GPU input modes across actual ownership,
direct/indirect draws, reversed creation order and the reciprocal managed OLD
blend/NEW cutout case; all GBuffer/depth channels match the baseline exactly.
Managed mesh156/raster189/world536 and native-history156 regressions pass,
validation errors0. Lower resource-key ties, ordering within NEW model groups,
decal auxiliary outputs, accelerated raster and final live/lifecycle acceptance
remain open; this is a bounded boundary correction. Detailed evidence is in
SE-Vulkan/Docs/LightingParity.md.

Final regressions for the OLD/NEW boundary correction also pass180 GPU-scene,
429 indexed-motion and262 indexed-attachment checks with validation errors0;
the broad scene's existing unused fragment-output warning remains. The next
NEW ordering oracle is stock first-seen LodInstance/state groups, each emitting
its preprocessed parts contiguously. Global per-technique phases alone do not
prove that behavior, so whole-goal completion remains unproven.

The NEW group audit reproduced red/blue ordering differences first in managed
submission, then specifically in GPU ownership when both models share one
geometry arena page. NEW batches now retain family/part identity; native-pass
groups preserve selected first-seen order, and NEW producers consume stock
preprocessed part order. The strengthened58 checks pass in both GPU input
modes, plus180 broad GPU-scene and141 OLD/NEW-boundary regressions, validation
errors0. Expanding the scene to A/B/A actors still fails managed submission:
the second A cutout is emitted after B. That group-identity work remains active;
mixed NEW ownership, per-view/state ordering, lower OLD resource ties, decal
auxiliaries, accelerated raster and final live/lifecycle acceptance remain open.
Full details and binary identities are in SE-Vulkan/Docs/LightingParity.md.

Managed A/B/A grouping now carries first-seen group and preprocessed-part order
through blob, pointer, retained-object and bulk queues, preserving physical
draw IDs. Per-instance appearance stays grouped; texture-binding variants split
and rejoin correctly. The expanded147 checks pass in both GPU input modes.
Actual bulk submission also exposed and corrected rejection of known actor
raster flags; conflicting raster flags remain rejected. Final regressions pass
156 mesh/189 raster/538 world/497 CPU-history/156 native-history/141 OLD-NEW
boundary/180 GPU-scene/429 indexed-motion/262 attachment checks, errors0.
Mixed NEW owners still render in separate ownership sequences and remain an
active gap, alongside lower resource ties, per-view/state order, decal
auxiliaries, broader accelerated raster and final live/lifecycle acceptance.

The mixed NEW-owner gap now has a confirmed pixel witness: actual NEW GPU A
overwrites actual NEW managed B, producing red[255,0,0] instead of the
blue-gray reference[162,162,199]. Ownership and NEW-provenance checks pass
before the pixel assertion fails. Evidence and preserved captures are recorded
in the parity journal; the next correction must unify NEW group order across
ownership producers.

Mixed NEW ownership now shares first-seen main-group/part order across managed,
indexed GPU and native-pass producers. The focused64 checks pass in both GPU
input modes, including all three owners, direct/indirect draws, owner zero and
exact covered GBuffer/depth references. Regressions147/141/180/429/262/156/189/
538/497/156 pass with validation errors0; detailed lanes and candidate hashes
are in SE-Vulkan/Docs/LightingParity.md. Root versus selected LOD/state order,
per-view and instance ties remain unverified. Lower OLD resource-key ordering,
decal auxiliary outputs, broader accelerated raster and final matched live
acceptance still keep the full goal open. Ordinary native assets remain unchanged.

The cached-cascade menu audit reproduced stale ownership: menu rendering clears
shared world images, then an update-mask-zero world frame reuses empty contents.
Native cache invalidation passes338 focused checks, but a stronger frozen GPU
owner test exposes a second boundary: managed view selection must also see the
invalidation. A CPU cache-mask query now carries that ownership into scheduling
before native/GPU depth views are built. Final458 raw-cache checks and204 scene
checks in each GPU input mode pass, including actual managed/native/GPU owners,
frozen1/6-cascade scenes and preservation of untouched far images. Regressions
pass206 scheduling,36 shadow-occlusion,538 world,123 native-pass and156 native
history checks; validation errors0. The reproduced cached-cascade menu issue
is closed in these paths. OLD resource-key and NEW dynamic ordering, decal
auxiliaries, broader accelerated raster and final matched live acceptance still
keep the full goal open. Details, both negatives and final identities are in
SE-Vulkan/Docs/LightingParity.md; ordinary native assets remain unchanged.

The OLD resource-order gap now has a matched live witness. Preloaded red A and
blue B retain stock LOD-mesh/material keys across reversed actor creation and
A recreation. DX11 renders all four panels blue; Vulkan renders three red,
with identical depth and metal/gloss/emissive in those ROIs. A dedicated live
gate verifies stock keys and exact color/depth, then fails columns0,2,3. The
small standalone64-check control passes the known failing renderer and cannot
qualify a fix. Actual resource-identity production and propagation remain open;
no product sort implementation changed in this proof slice. Full evidence and
the separate HDR/DX11-disposal limitations are in the SE-Vulkan parity journal.

CPU OLD identity building blocks now match the installed stock RK/material
allocator, bit packing, descriptor conversion and shared LOD-index freelist:
7959 checks pass on both Framework and .NET10. Production header/path work runs
without a platform renderer and leaves stock material/proxy/resource queues
untouched. Retained model/LOD/layout ownership and draw-key propagation still
need integration; the failing four-panel live ordering gate remains open.
These CPU checks do not qualify that visible gap. Details and identities are
recorded in SE-Vulkan/Docs/LightingParity.md; no native/shader ABI changed.

2026-10-21 full shader-parity candidate foundation: shared structured manifests
and the stage catalog now exist in both Anomaly adapters. Required-program and
duplicate ownership rejection preserve existing registration; target formats,
queued HDR/negative-alpha clears, stable resize IDs and timeline retirement
have focused CPU/GPU coverage. Feature bits13/14 coordinate native resource
commands and execution status without changing ABI-1 layouts. The native suite
passes all17 tests with validation available; repaired actual-core baseline
passes274 checks. This is the registration/resource foundation only. Fullscreen
equations/history/Chain, shared frame/environment, portable temporal callbacks,
all named-stage execution and true material-owned attachments, HDR/jitter/vendor
integration and matched Windows live/performance acceptance remain open.

### Shared frame/environment parity — 2026-10-21 candidate

DX11 and Vulkan now use the same device-free lighting and motion-safety
calculation. Anomaly receives raw sun color, diffuse/ambient factors, normalized
toward-sun direction, planet air/visual radii and camera deltas formed from
double-precision absolute positions. Stock Vulkan light inputs are unchanged.
The guest boundary also normalizes/negates stock direction when no adapter input
is present. Native feature bit15 carries a checked96-byte environment packet
keyed to the current open renderer frame. Fullscreen b6 preserves safety zero
and publishes planet/skip constants.

FrameTemporal getters read the callback's immutable frame-index snapshot; they
no longer perform wall-clock-based native queries. Camera cuts invalidate the
current history immediately and reset the shared motion envelope. The shader
probe verifies actual b6 output for raw color, direction, zero safety and planet
radius. CPU tests cover rotation,80m cuts,1/16 recovery,1e12m origins and repeated
getters. Native17-test suite passes; actual-core depth/volume/recreation suite
passes302 checks with validation enabled and no reported errors. Full matched
Windows adapter/visual/performance acceptance is still required.

### Fullscreen isolation, histories and output — 2026-10-21 candidate

Vulkan fullscreen passes now render an isolated product first, publish that
product and capture its history, then compose using Anomaly's DX11 merge
shader equations. Add/sub preserve destination alpha; mix saturates source
opacity. No stock blend mode is changed. Chain state is local to its insertion
slot and a final Chain is copied into the actual destination. The frame holds
registration/settings while copying commands, and publishes all previous-frame
aliases before building any consumer. Disabled/re-enabled and resized histories
start invalid; production checks native recording/frame age before reuse.

Half/quarter dimensions use DX11 floor division independently of renderer DRS
rounding. AfterUpscale uses output dimensions. Native feature bit16 supplies
a renderable, bilinear output for requested AfterUpscale work when no upscaler
is active, plus exact output readback. Owned HDR history alone does not request
that extra output path. Renderable display images include color-attachment
usage. Offscreen consumers snapshot the current tone/upscale image correctly.

Evidence:2,800 independent GPU composition pixel comparisons cover every merge
equation, negative/over-one alpha, HDR, destination alpha and odd-size scaling.
Maintained packet tests cover all compose histories, early consumers, Chain
completion and slot separation. The actual-core324-check lane verifies final
AfterUpscale Chain pixels and dimensions at50/75/100% without an upscaler in both synchronous and pipelined recording, with
validation enabled and zero reported errors. Full combined Windows live visual
and performance acceptance remains pending.

### Stage 5 portable callbacks and temporal production

RegisterPortable closes renderer-free preparation and queued clear/copy/pixel
fullscreen/velocity commands on both hosts while preserving DX11 Register.
Vulkan ordinary passes now produce reactive/velocity updates with the DX11
helpers and distance-alpha scaling. Frame settings/history remain frozen;
callback uniforms can be prepared before drawing. Native bit17 adds R16 render
target execution. GPU contracts pass with validation enabled and zero errors;
DX11 callback compatibility passes for net48/net10. Aurora/cloud/contact-shadow
preparation is migrated, and Prism normal rejection/history caps/0–7 iterations
are corrected. Production guest Temporal/Moments pass D3D11 WARP readback for
rotation, rejection and exact ages. Loaded-world combined acceptance is open.

### Stage 6 captured sources and canonical stage model — partial

Both hosts now consume captured manifest/shader/include bytes. Renderer selection
cannot recapture an edited original root. The shared model covers26 stage names,
Lighting-family injection, .h headers, ownership and forbidden Depth/MRT rules;
DX11 consumes that model directly. GetCapabilities reports registration/support
separately and keeps Vulkan named-stage/same-draw attachment support false.

Native resource-free fullscreen shaders now use format-aware variants. A real
captured pack executes its original HDR/negative-alpha pixels after its source
is edited, with validation enabled and zero errors. All current pack pipelines,
the isolated native17-test gate and329 actual-core checks pass. The compiler
gate passes39 checks; GPU-owned vertex/pixel generation retains stock provenance
and identical SPIR-V for a comment-only override. This closes the immutable
source/compiler foundation. Native owner activation, every-stage execution,
complete attachments, HDR/vendor integration and matched Windows acceptance
remain open.

### Vulkan owned-raster generations (stage 6, partial)

Anomaly now prepares detached raster generations for `Anomaly.CameraVelocity`,
`Anomaly.LinearDepth` (including HiZ) and `Anomaly.HistoryColor`. Sources and
includes are captured with pack identity, owned/game shader bytes, injection order and pack
defines. Overlapping overlays fail closed to stock; conflicting exclusive packs
are disabled as on DX11. A replacement of shared `Fullscreen.hlsl` affects every
owned raster consumer in the same transaction. The adapter explicitly converts
DX11 clip-space Y for Vulkan's positive viewport while preserving custom UVs. Native lit-mip blits
are outside this raster contract and do not execute arbitrary HistoryCopy code.

Preparation compiles and creates new pipelines without changing the active
programs. Before the frame snapshot, Anomaly rechecks registration revision,
device epoch and program identity, then activates the complete generation under
one lock. Compilation, native descriptor rejection or stale preparation retains
the prior valid generation. Unregister prepares stock restoration. Pipeline-only
retirement preserves shared output/history images and uses SE-Vulkan's existing
submission-completion retirement. Device recreation recompiles the captured
active sources and rejects prepared handles from the old epoch.

`ShaderPackRegistry.GetOwnedStageState(stage)` reports `RasterReplacementSupported`,
`RequestedGeneration`, `PreparedGeneration`, `ActiveGeneration`, `ProgramIds` and
`Error` for this Vulkan subset. Use `FullscreenPassRegistry.GetProgramState(id)`
for each listed program: `ActiveGeneration` identifies its generation, while
native execution counters/completion/current-frame production remain separate.
Preparation and activation do not imply execution. A null active generation
means the initial stock program, before any generation transaction.

The deterministic Vulkan fixture proves captured pixel replacement, named
vertex orientation at odd dimensions, injection/defines actually used by the
pixel shader, HDR/negative alpha, native execution, compile/binding rollback,
stale source/device refusal and stock restoration, with validation enabled and
zero errors. This is a partial execution gate. Complete stock-native stage
activation, reserved catalog bindings, same-invocation attachments and matched
loaded-world acceptance remain open; the aggregate named-stage capability
stays false.


Owned-raster replacement requires native feature bit18 in addition to bit13
retirement. Older hosts receive an explicit update refusal. ABI-1 packet layouts
are unchanged. Reloading identical captured sources coalesces to the active
pipelines. Shared-volume generated requests now capture each provider's declared
pack root (including sibling Inject/ headers), or its standalone directory;
active includes escaping capture are still refused by the compiler.

The final actual-core suite passes355 checks with synchronization validation
and zero errors. It executes real Fog/Clouds providers and the owned-stage
synchronous/pipelined replacement/rollback/restoration path with an outstanding
submission. A matched loaded-game DX11/Vulkan comparison is still required.

## Vulkan native postprocess generations (partial stage 6)

Coordinated Anomaly/SE-Vulkan hosts with native feature bit19 prepare detached
stock-postprocess pipeline generations. Anomaly remains the pack host; SE-Vulkan
owns compilation, native activation and completion-based retirement. The
renderer invokes preparation before postprocess constant configuration and the
immutable frame snapshot. A source revision or device mismatch discards both
owned-raster and native candidates; a failed native commit retains both previous
generations and the next attempt rebuilds discarded owned candidates.

Postprocess entry-point overlays, include-only overlays, injections and legal
boolean defines use captured game/pack bytes through the existing authenticated
compiler. Canonical header overlays rebuild their named family; uncatalogued
shared postprocess headers conservatively rebuild all mapped families. HBAO
negative-border variants belong to the same transaction. Conflicting overlay
claims fall back to stock. Failed compilation, unsupported descriptors, invalid
compute workgroups or an incompatible fragment interface refuse the candidate.
Unregister restores stock. HDR format changes invalidate preparation even when
registration is unchanged; identical source/format generations are reused.

`ShaderPackRegistry.GetNativeStageState(stage)` separates requested and prepared
identities from per-variant `ActiveToken`, `ActiveGeneration`, `Executions`,
`LastProducedFrame` and `LastSubmissionCompleted`. Registration, compilation and
activation are not execution evidence. Existing ABI-1 packets are unchanged;
new module/status transports validate size/version and older hosts receive an
explicit update refusal. Host constant packing remains the stock layout;
immutable derived metadata is published separately at activation.

This is an initial native-postprocess owner implementation, not complete named
stage parity. Extra catalog descriptors remain unsupported on these owners.
Geometry, lighting, transparency, shadows, atmosphere, decals, particles,
probes, foliage, complete reserved bindings and same-invocation attachments
remain open. Aggregate named-stage and same-draw attachment capabilities stay
false. Matched Windows loaded-world visual/temporal/performance acceptance,
stage7 HDR/display integration and stage8 lifecycle qualification remain open.

Native postprocess evidence now includes398 actual-core checks and a separate
380-check native family gate, both with synchronization validation and zero
errors. The family gate activates all78 catalogued postprocess variants under
one authenticated generation, verifies each executes, and retains the existing
stock pixel/DRS/odd-size/HDR checks. A resource-free HBAO coarse replacement
produces quarter AO in all32 clamp/border permutations and restores exact stock
AO. When reflection proves no quarter-depth texture is read, the authenticated
base variant serves both modes; sampled HBAO variants retain the authenticated
border transform and its refusals for incompatible fetch rewrites. This is
execution coverage for the current native postprocess catalog, not every
advertised named stage or unrestricted catalog bindings.

## Vulkan deferred lighting generations (partial stage 6)

Native feature bit20 adds the four deferred lighting fragment variants to the
same captured-source transaction as native postprocessing: directional, tiled
point, spot and shadowed spot. IDs256–259 extend the existing size/version-checked
module/status transport without changing ABI-1 layouts or reflection method
signatures. `Lighting` family injections precede stage injections; shared lighting
headers rebuild lighting owners. Anomaly remains the sole pack host. The native
compiler uses authenticated stock inputs and the existing variant defines.

One native batch builds every selected lighting/postprocess pipeline before any
activation. Compilation, descriptor/interface rejection, stale source/device
state or a changed HDR format retains the prior generation. Both HDR pipeline
formats are prepared. Replaced pipelines retire after submission completion;
renderer-owned lighting descriptor layouts are shared and remain renderer-owned.
Unregister restores stock through the same transaction. Exclusive conflicts are
resolved before capability checks, so inactive conflicting owners retain stock.
Active lighting requests and global pack defines receive an actionable bit20
refusal on older hosts, before shader assets or GPU work are needed. Native named
variants no longer inherit the owned camera-velocity `ANOMALY_COMPOSITE` define;
explicit pack defines retain their existing boolean semantics.

`ShaderPackRegistry.GetNativeStageState("Lighting")` reports all four variants;
individual `Lighting.Dir`, `.Point` and `.Spot` queries remain available. Each
variant now exposes `Profile` alongside source, active identity and execution/
completion/frame state. These variants are `ps_5_0`. Legacy diagnostic lighting
replacement clears the native generation identity, preventing stale active
fingerprints from appearing in the status query.

This slice replaces/injects fragment programs within the existing native owners.
Lighting vertex/geometry overrides, complete reserved catalog bindings and
same-invocation attachment output remain open, along with the other native
owners. Aggregate named-stage and same-draw attachment capabilities remain false.
The full actual-core gate passes434 checks with zero core/synchronization
validation errors, including expected intermediate HDR pixels for all four
lighting variants, family/stage injection, shared-header reload, pipelined
replacement, HDR changes, mixed lighting/postprocess rollback, late batch failure
cleanup, exact stock restoration and completion-based retirement. Matched
loaded-world DX11/Vulkan visual, temporal and performance acceptance remains open.

The final focused actual-core rerun passes104 checks, including the define-scope
shader guard and explicit fragment profiles. The separate382-check native family
gate executes all82 selected postprocess/lighting variants and retains stock pixel
checks, with zero validation errors. These are deterministic GPU gates; loaded-world
acceptance and the remaining native contracts are still required.


### Shared binding facade and early products

ShaderBindRegistry now exists in the device-free core with the DX11 reflection
signature and register constants. Pending reservations replay on DX11 selection.
One shared immutable model owns slot allocation, fixed velocity reservations,
idempotent automatic requests and conflict rollback; snapshots copy only on
registration changes. Vulkan StatusLine explicitly reports unavailable native
binding execution. Native reserved catalog descriptors, complete b6 binding and
typed/null resource semantics remain open.

Native bit21 executes CameraVelocity, LinearDepth, HiZ and ReactiveClear after
GBuffer/object-motion/decal work and before AO/lighting. Public callback phases,
HistoryCopy and ABI-1 packet layouts stay unchanged. Legacy optional 2D fallback
texels now clear to RGBA zero; missing required extended inputs still refuse.
This does not close null dimensions, typed/array/volume/buffer fallbacks or full
named-stage support.

The active goal no longer requires Windows live acceptance. Remaining native
contracts and automated execution, lifecycle, temporal, compatibility and
performance qualification remain completion gates. Loaded-world Windows
qualification stays separately tracked.


### Native owner resource bindings - bit22

The previously unavailable native binding path now runs on the existing lighting,
postprocess and HBAO owners. Requests and previous-history references freeze before
callbacks; native copied rows validate frame/version/owner/mask data. b6 shares the
fullscreen extras writer and current velocity availability is independent of
history validity. Catalog reads preserve type/dimension checks, frame production
and persistent target semantics. True typed nulls match independent DX11 WARP
queries/loads. Global initialization clears precede native owners and preserve
FP16 alpha -1. Specific DX11 lighting hooks use Dir/Point/Spot names.

Custom lighting layouts retire with pipelines; base-only restoration reuses
engine layouts/sets. Typed native binding status distinguishes registration,
copied descriptor records and native shader execution/completion. The focused194
resource checks pass with zero core/synchronization errors, including four
lighting and four compute-tone variants, both HDR modes, HBAO, current/history
inputs, mutation timing and stock restoration. Old bit21 hosts actionably refuse
new resources before allocating native generation candidates. The combined gate
passes664 checks; the separate family gate passes382, including all82 variants
and stock highlight output. Both report zero core/synchronization errors.
Catalog lifecycle cleanup is qualified below by the102/766-check gates.

Remaining: other native stage owners and vertex/geometry programs; full
same-invocation attachments and named formats; HDR/jitter/vendor stage7;
automated lifecycle/temporal/visual/performance stage8. Catalog publication
ownership/case handling and ordinary PublishedBuffer release on unpublish/reload
are covered by the separate cleanup change below. No aggregate named-stage or
same-draw capability is enabled by the resource transport.


### Core catalog publication ownership and cleanup

Core publication names and pack ownership now use case-insensitive DX11 rules.
Malformed/reserved/competing publications fail closed. Requested Vulkan targets
share ownership with ordinary uploaded publications. Replacement, Unpublish,
UnpublishAll and public pack unregister queue all owned images for native
retirement; cloned CPU payloads survive until explicit Clear and can be republished.
Pending renderer selection replays publications; raw borrowed DX11 resources keep
their existing ownership semantics. Legacy target formats0/1 now preserve existing
RGBA8/FP16 callers, including actual cloud history's alpha -1 initialization.

The focused actual-core lifecycle gate passes102 checks, including an in-flight
GPU read before replacement, texture/structured/mip-volume cleanup, both target/
upload transitions, pending selection and eight unregister cycles preserving a
second pack. The final combined gate passes766 checks with validation enabled and
zero core/synchronization errors. Memory plateau/performance qualification remains
open, alongside DX11 target format/disposal integration, cached-wrapper liveness
qualification, the history correction below and the remaining native owners,
full attachments and stage7 display/temporal integration. Aggregate capabilities
remain false. No quality settings or player profiles were changed.


### Reserved color-history timing and DRS parity

Vulkan now freezes one previous historyColor reference per frame/device epoch and
shares it between native rows, fullscreen passes and portable callbacks. It checks
producer identity, exact preceding production, camera validity, size and epoch.
HistoryCopy keeps its named shader path and copies command values before callbacks,
then records/publishes after all AfterUpscale programs, Chain presentation and
AfterFullscreen callbacks. First use and skipped/resized producers reject and
recover after a valid frame. Capture uses raster HDR dimensions, matching installed
DX11 EnsureHistoryTarget/ResolutionI and resolution-change invalidation; ordinary
AfterUpscale retains output dimensions and DX11 floor/Replace rules.

The maintained GPU fixture reproduced the former current-frame late read before
repair. A separate CPU fixture reproduced the former output-sized capture at DRS.
The expanded actual-core gate passes125 checks: exact prior values at all three
consumer positions, current tail-capture HDR pixels, dimensions, nonzero recovery,
disable/re-enable, odd resize, all three DRS fractions and both recording modes.
The final combined gate passes891 checks with validation enabled/errors0. All six
CPU contract groups and net48/net10 builds pass. No native ABI/capability bump was
needed; native library/source remain those qualified by the382-check/all82 native
family gate. Other native owners, complete attachments, DX11 target integration,
classic fullscreen null-dimension semantics, stage7 and stage8 remain open.


### Shared catalog target/facade qualification

Both hosts now use one device-free names/reservation/color-format contract. Core
exposes all20 constants and matching PublishedBuffer properties, methods and
constructors; raw GPU publication remains DX11-only. Portable structured metadata
reports element count/stride/availability; Vulkan GPU references stay opaque.
Native target snapshots report known DXGI formats and invalidate after logical
replacement or changed allocation dimensions.

DX11 honors six canonical formats plus legacy0/1, defers device/worker requests to
its renderer queue and frame-begin flush, and returns owned textures through
Keen's RwTextures.DisposeTex. Failed allocation retains the prior upload/target
and requested size. Shared ownership gating and retirement callbacks prevent
cross-facade conflicts and replay resurrection. Requests made before renderer/
device selection replay afterward. Old satellites receive an update refusal before
misinterpreting a format; resource availability never implies GPU completion.

The actual-core/linked-production DX11 WARP fixture passes134 checks on net48 and
net10, including typed GPU pixels, reflection, pending cancellation, allocation
rollback, deferred reads, actual device replacement, worker retirement and16
unregister cycles returning pool counts to zero. Its pooling adapter is not a
loaded-game manager claim. Native focus105 and final combined894 pass with zero
core/synchronization errors. All six CPU groups and both managed builds pass.
Native C++ sources/library are unchanged. The remaining catalog work is upload-mode
transition qualification and classic fullscreen true-null/dimension behavior;
remaining stage6 owners/attachments, stage7 and broad automated stage8 stay open.


### CPU catalog upload transitions — 22 October

Shared device-free validation rejects malformed RGBA/mip/structured input before
replacing a valid publication. Mode switches reset Depth/stride and release
inactive retained CPU payloads. Volume mip copies strip per-level padding to
match DX11's subresource pitches; recreation reads the exact expected non-base
mip after caller-array mutation. Borrowed DX11 resources retain caller ownership.
WARP143 checks pass on net48/net10; native catalog109 passes with zero validation
errors. The six CPU contract groups and both managed builds pass. Final combined
GPU acceptance passes898 checks with validation enabled and zero errors. Native C++/ABI/library are unchanged. Classic
fullscreen null dimensions, remaining stage6 owners/attachments, stage7 and
automated stage8 qualification remain open.


### Classic fullscreen optional SRVs — bit23, 22 October

Reproduced missing Texture2D as1x1 on the old classic Vulkan bus against independent
DX11 zero-dimension controls. Native fullscreen sampled/structured/typed SRVs now
use genuine null descriptors, with reflected dimensional/numeric compatibility
checks and frame-local descriptor storage. Compatible image/buffer uploads still
execute; typed buffers remain distinct from structured uploads. Extended pass
inputs stay required. ABI-1 packets are unchanged. Device nullDescriptor support
and native bit23 are explicit requirements; old hosts receive update guidance
from executable SPIR-V bindings before pipeline allocation. Optimized-out unused
resources retain older-host compatibility. Shared ClassicNullResourcesSupported
and aggregate fullscreen support are device-qualified.

The focused actual-core GPU gate passes100 checks with zero validation errors;
seven older-bit22-host checks pass. Both managed targets, six CPU groups and
canvas TypeScript pass. Native CTest executes16 passes plus the configured
gpu_opaque_reference skip. Final combined998 passes with validation enabled and zero errors.
Other stage6 owners/attachments, stage7 and broad automated stage8 remain open.


### Environment-family native owners (focused execution gate, 22 October)

Native bit24 adds captured atmosphere compute/main-view/probe owners512..514 to
the shared preparation/activation transaction. Changed compute generations dirty
cached LUTs while preserving their actual image layout and synchronization. Probe
atmosphere retains its two-target format and scope-specific stock inputs. Native
bit25 adds foliage768..772: one shared vertex module and two geometry/pixel pairs
form an indivisible generation closure. Each raster invocation binds the merged
module resource contract; execution counters identify every participating stage.
Layouts and pipelines retire together after their last completed submission.
Catalog t5..t9 and b6 extras remain shared host contracts. ABI-1 packets retain
their layouts. Older hosts receive coordinated-update refusal.

The integrated environment owner batch passes170 actual-core GPU checks with
core/synchronization validation enabled and zero errors. Independent outputs cover
LUT/main-view atmosphere, both probe MRTs on all six faces, and shared foliage VS,
each GS and each PS. Vertex/geometry/pixel-only catalog resources, first-use s1,
probe-scope constants, immutable extras, both HDR formats, synchronous/pipelined
execution, incomplete closure/interface/compile rejection, exact stock restoration
and completion-based retirement are covered. Managed net48/net10 and native builds
pass. The previous combined998 gate predates this batch; it is not expanded proof.
Aggregate named-stage/attachment capabilities remain false. Remaining stage6
owners/attachments precede stage7 and stage8, including matched Windows loaded-world
acceptance. Native mip blits do not establish arbitrary HistoryColor execution.

The coordinated native binding transport accepts all87 current owner rows within
its90-row bound; an oversized packet leaves the copied rows unchanged. Eight
real older-bit23-host checks prove actionable bit24/25 refusal before compilation
or native candidate allocation. This is focused stage6 evidence.


### Native decal pair generations — focused stage6 execution gate

Native bit26 adds five captured stock VS/PS pairs (1024..1033), corresponding to
native decal roles1,2,3,7,8. A pair prepares and activates together; fragment
invocations carry the merged vertex/pixel resource contract. The original
indexed-volume invocation, dynamic b2 batch offsets, per-channel GBuffer masks,
read-only depth and transparent OIT outputs remain native-owned. Catalog requests
can explicitly replace material t5/t6; absent requests retain their stock inputs.
Pipeline/layout retirement follows the last completed submission. ABI-1 packet
layouts remain unchanged. The module/row transport bound is100 (92 actual binding
owners after shared-module aliases merge). Older hosts require coordinated bit26
support before pack compilation or native candidate allocation.

The focused98-check actual-core GPU gate proves all ten modules with independent
GBuffer and OIT output equations, vertex/pixel catalog inputs, dynamic b2, atomic
activation, failed-compilation/incomplete-pair rollback, exact stock restoration
and retirement. Each role produces exactly matching synchronous/pipelined output, with zero
core/synchronization errors.
The updated170-check environment gate passes with all92 binding-owner rows and
oversized-packet preservation. Ten real older-host checks cover bits24/25/26.
Native CTest executes16 passes plus its configured reference skip; managed net48/
net10 builds and focused CPU native-stage/binding contracts pass. The extended
full recreation gate now includes these fixtures but has not been rerun for this
batch. This evidence does not establish every legal shader interface, other native
stages, full attachment semantics or parity. Both aggregate named-stage and same-draw attachment flags
remain false. Stage7 and stage8 follow the remaining stage6 work.


### Native probe processing generations — focused stage6 execution gate

Native bit27 extends the generation transaction to captured probe-processing
owners1280..1283: prefilter, forward sky, six-MRT publication copy and stock-dormant
factor blending. Selected generations activate at one frame boundary and old
pipelines/layouts retire after completed submission. Native scope binds the
actual probe b0 and complete144-byte forward b2 (view, projection, sun, size)
when a replacement reads beyond the stock64-byte view. Stock uploads retain
the64-byte path.
Catalog t5..t9 overrides are explicit; unrequested stock input slots retain their
ownership, including the publication copy's sixth-face t5. Native mip/base-level
image copies remain separate from named shader execution. ABI-1 packets retain
their layouts; module/row capacity is104 with96 possible descriptor owners.

Installed DX11 MyEnvironmentProbe fixes BLEND_USING_FACTOR=false and invokes
EnvProbeCopy with blendStrength, so EnvProbeBlend can activate but has zero GPU
execution in that stock path. Its compilation/activation must never imply
production. This does not claim dispatch support for dormant EnvProbe compute
entry points or complete geometry/EnvProbe parity.

The focused326-check actual-core GPU gate passes independent per-face/per-mip
equations on all six probe cubes, exact synchronous/pipelined outputs, held-capture
execution status, frame-copied catalog changes, invalid compilation/MRT rollback,
exact stock restoration and retirement with zero validation errors. The existing
stock-probe regression passes427 checks with zero errors, and six real bit26-host
checks prove actionable bit27 refusal before compilation/allocation. Both managed
frameworks and the native build pass. The final combined496-check environment/probe gate passes all96 binding-owner
rows and oversized-packet preservation with zero errors. Native CTest and focused
CPU native-stage/binding contracts pass; the broader recreation suite includes
the new fixtures but has not been rerun for this batch.
Both aggregate named-stage and same-draw attachment capabilities remain false;
remaining stage6 work precedes stages7 and8, including Windows live acceptance.


### Native particle generations — focused stage6 execution gate

Native bit28 adds the complete particle closure1536..1545: eight compute variants
and the shared VS/PS render pair. A selected particle source rebuilds the entire
list-protocol family and activates it atomically at a frame boundary. Old
pipelines/layouts retire together after completed submission. Workgroup sizes,
64/464/4-byte native storage layouts, typed argument formats and stage-specific
inputs validate before activation. Stock buffer ownership and OIT/depth behavior
remain native. VS descriptor bindings retain their1024-offset relocation; merged
logical t5..t9 reservations and b6 extras bind the corresponding physical slots.
The module/row bound is114 with105 possible binding owners after aliases merge.

Direct nonzero dispatches report submitted participation separately from
completion. Indirect simulation/raster participation is credited only when
completed GPU argument readback proves nonzero X/Y/Z and index/instance counts,
with the original
frame and matching generation token. Empty work cannot advance those execution
records; stale generations cannot credit a newer program.

The checked40-byte ABI-1 particle readback request validates size/version, buffer
kind, aligned range and a64KiB bound outside recording. The diagnostic native
buffers stay renderer-owned; this adds no pack discovery or execution host.
Older hosts actionably refuse bit28 before compilation, candidate allocation or
readback symbol lookup. Existing packet layouts and DX11 callback signatures stay
unchanged.

The focused84-check actual-core GPU gate proves all eight compute modules with
independent GPU sentinel writes and the raster pair with expected OIT pixels,
VS/PS/compute resources, empty index/instance/X/Y/Z participation and recovery,
wrong workgroup/
stride/stage-input refusal, malformed readback size/version/range, rollback, exact
stock restoration and retirement. The existing216-check stock transparency and
particle lifecycle gate and updated170-check environment/105-row transport gate
pass, all with zero core/synchronization errors. Seven real previous-bit27-host
checks pass. Both managed frameworks, native CTest and focused CPU contracts pass.
The extended full recreation fixture compiles but has not been rerun for this
batch. Both aggregate flags remain false; this does not close all legal shader
interfaces, remaining native owners, same-invocation attachments or full parity.


### Native screen-shadow generations — focused stage6 execution gate

Native bit29 adds CSM evaluation1792 and optional per-view usage statistics1793
to the captured-source generation transaction. Each native owner receives its
actual frame/screen/cascade inputs, shared reserved t5..t9 and b6 extras, fixed
16x16x1 dispatch contract and native output. Activation moves complete pipeline/
layout bundles at a frame boundary; retired generations wait for completion.
ABI-1 module/state packets remain unchanged. Capacity116 includes107 possible
binding-owner rows. Host capability checks now follow ascending feature versions
so global defines report the first unavailable owner contract consistently.

DXC encodes HLSL unorm<float> storage as R32f. The derived CSM executable adapts
that storage-image type to the owning R8_UNORM target and adds the required
StorageImageExtendedFormats capability, already enabled by SE-Vulkan. Original
authenticated shader artifacts remain intact. Other storage formats are refused.

The43-check actual-core GPU fixture passes independent CSM UNORM equations, exact
per-cascade usage counts, frame catalog updates, atomic activation, synchronous/
pipelined output, inactive usage status, compile/workgroup rollback, exact stock
restoration and retirement with zero core/synchronization errors. Native and
managed net48/net10 builds pass. Six real bit28-host refusal checks, the170-check environment/107-row transport
gate and36-check stock shadow-occlusion regression pass with zero validation
errors. Native CTest executes16 passes plus the configured reference skip;
focused CPU contracts and the developer canvas check pass. The extended full
recreation fixture compiles but has not been rerun for this batch.
This covers two screen-space compute owners; shadow geometry, other Shadows
sources and full same-invocation attachments remain separate stage6 work. Both
aggregate capability flags remain false. Stage7 and stage8 follow stage6.


### Shared native source dependencies — focused stage6 execution gate

The device-free source transaction selects native consumers by include dependency,
including dependencies across named-stage families. Native catalog rows retain
their first three reflection fields and append authenticated stock dependency
paths. Core also walks copied source includes after publishing every unique
override, so replacement programs can introduce dependencies on other packs'
headers or entry-point sources. Relative, transitive, quoted/angle includes and
line continuations are resolved; comments and cycles do not invent dependencies.
Macro includes and older three-field catalog rows choose conservative selection.
Competing overrides retain existing conflict handling. Generic shared header
extensions are eligible even outside the currently mapped native stage folders.
The generation fingerprint contract advances to the captured-include-closure
revision; complete selected closures still activate and roll back atomically.

The21-check actual-core GPU gate changes only Shadows/Csm.hlsli: all authenticated
CSM consumers in shadows, lighting, decals and particles join the same generation,
selected shadow pixels change to independently expected values, unrelated tone
mapping stays untouched, and synchronous/pipelined output agrees. Header reload,
invalid-header rollback, exact stock shadow/HDR restoration and dependent-layout
retirement pass with zero core/synchronization errors. The170-check environment
integration also passes; focused CPU contracts cover dependency selection and
older catalog fallback. Managed net48/net10 builds pass. Native ABI/capability
bits are unchanged. This covers the current native owner catalog; missing owner
families and legal interface/attachment contracts remain stage6 work. Both
aggregate capability flags remain false. Stages7 and8 remain ordered afterward.


### Native OIT resolve generation — partial stage6 owner

Native bit30 adds owner2048 for `Transparent/OIT/Resolve.hlsl`. Anomaly captures
and selects the source transaction; SE-Vulkan compiles its authenticated stock
variant or derived replacement, prepares both HDR pipelines and the reflected
layout together, activates at the frame boundary and retires the replaced bundle
after submission completion. Existing ABI-1 packets stay unchanged. The module
and binding-row bound is117, with108 possible native descriptor owners. Older
hosts refuse OIT overlays, Transparent injection and global defines actionably.

The actual weighted-OIT publication invocation supplies accumulation/coverage
inputs, reserved catalog t5..t9, frame b0, extras b6 and stock samplers. It preserves
native blend/raster semantics. Completed execution is reported only when the
native transparency scope actually draws; activation alone is insufficient.
The current builder requires a float4 target preserving blend alpha. Wider legal
interface compatibility, transparent geometry VS/PS and arbitrary attachment
outputs remain stage6 work; this owner does not complete the Transparent family.

The32-check actual-core GPU fixture proves independent HDR blend values, catalog
clears and extras, both HDR formats, exact synchronous/pipelined agreement,
no-work execution honesty, failed compilation/layout rollback, exact stock
restoration and completed retirement, with zero core/synchronization errors.
Six actual previous-bit29-host refusal checks pass. Native mip blits remain copy
operations, not arbitrary HistoryColor shader execution. Both aggregate named-
stage and same-draw-attachment capabilities remain false. Finish remaining stage6
owners and full same-invocation attachments before stages7 and8, including the
required matched Windows loaded-world acceptance matrix.

The170-check environment integration passes with108 binding-owner rows,117-row
acceptance and118-row rejection preserving copied rows. The216-check stock
transparency/particle lifecycle regression and native CTest (16 executed passes,
one configured reference skip) pass. Both managed target frameworks and focused
CPU native-stage contracts pass. The full recreation fixture compiles with this
new fixture; the broad combined recreation suite was not rerun for this batch.


### Fixed-layout stock-world generations — partial stage6 owner contract

Native bit31 adds logical VS/PS owner IDs `4096 + 2*pipeline` and
`4097 + 2*pipeline` for the initialized stock world pipeline catalog. Existing
ABI-1 module/state packets remain unchanged; the checked generation module bound
is8192. The signed managed flags retain all32 native feature bits. Previous
bit30 hosts refuse geometry overlay, injection and global-define requests before
compilation or candidate allocation. Depth injection remains forbidden by the
shared parser; GBuffer replacement still requires its existing exclusive owner.

The catalog authenticates captured and supplemental variants and selects by
actual GBuffer/Depth/Forward/Highlight pass header, avoiding ambiguous shared
material entry-point ownership. Derived supplemental requests preserve their
stock defines and authenticate installed sources. Native generic-instance tint
adaptation remains present in active and restored variants. VS/PS pairs enter the
same source transaction and activate atomically at a closed-frame boundary.
The builder prepares complete main/cascade/spot bundles; replaced resources
retire after submission completion. Actual world, probe, highlight and spotlight
draw sites report their selected owner. Parallel geometry recording updates
production counters atomically. Stock initialization invalidates obsolete
geometry generation metadata.

The focused57-check actual-core GPU gate proves replacement pixels for rigid,
skinned, generic-instanced, cube and deformed-cube classic material streams in
both recording modes; atomic ten-module activation, empty-frame honesty, failed
compilation rollback, exact stock restoration including generic tint, and
completed retirement pass with zero core/synchronization errors. Its private
catalog deliberately isolates those five streams. It is not evidence that every
cataloged shader executes. Eight real previous-host refusal checks pass.

Geometry currently retains the fixed stock descriptor interface; additional
reserved catalog resources and the stage-specific geometry constants still need
integration. The DX11 geometry constants must not be replaced by fullscreen
extras merely because both use register b6. Native-cell and GPU-owned geometry
owner generations are described below; remaining legal interfaces and
same-invocation attachment outputs remain required stage6 work. Both aggregate capability flags stay false.
Native mip blits remain copies, not arbitrary HistoryColor shader execution.
Stages7 and8 follow stage6, including the required matched Windows live matrix.


The separate59-check unfiltered GBuffer family gate uses the productioncatalog:
1,120 material/decal/fade/voxel modules activate atomically, Depth stays untouched,
the five drawn streams retain expected pixels, rollback and exact stock/tint
restoration pass, and retired bundles collect with zero validation errors.
The complete stock-world catalog contains1,316 modules. Family-wide construction
and activation do not prove execution for every shader or geometry path.

The71-check actual-core raw Depth replacement gate proves independent depth
values in real cascade and spotlight invocations across the five classic streams,
exact recording-mode agreement, atomic pair activation, rollback, exact stock
restoration and retirement. Native depth-owner replacement invalidates cached
cascades and frozen spot maps; a zero caller redraw mask cannot suppress required
shader-change recovery. Ordinary unchanged shadow-cache reuse reports its prior
production frame and no new shader execution. Depth injection remains forbidden,
and attachment extensions do not gain permission to change stock Depth passes.

The final focused GBuffer gate has57 checks. Managed net48/net10 builds, focused
CPU native-stage contracts, native CTest (16 executed passes plus one configured
reference skip) and canvas TypeScript pass. Full recreation source now includes
both new fixtures and builds; that broad combined suite has not been rerun.
Remaining stage6 contracts and the mandatory stages7/8 Windows live acceptance
remain open; both aggregate capability flags remain false.


### Native-cell and GPU-indexed geometry generations — partial stage6 owner contract

An optional size/version-checked extension leaves the ABI-1 packets unchanged.
`sevk_native_owner_capabilities` fills a 24-byte record: flag1 native cells,
flag2 GPU NEW, flag4 GPU OLD, a module bound of 8192 and 117 binding rows.
`sevk_prepare_native_generation` takes a 32-byte request with the existing
32-byte module records. Its flag1 marks stock restoration; unknown flags, wrong
sizes, unknown ABI and empty module sets are refused before candidate
allocation. Owner spaces alternate VS/PS: native cells `[16384,16440)`, GPU NEW
`[32768,32824)` and GPU OLD `[49152,54464)`. Only initialized pipelines appear
in the catalog. Anomaly requires flags 1/2/4 for geometry overlay, injection and
define requests. It closes VS/PS pairs in each owner space and refuses older
hosts before compilation.

Detached builders keep the actual stock layouts, rasterization and admission
rules, and build back/no-cull pairs. One generation switches the live classic,
cell and GPU handles at a closed-frame boundary. Replaced handles retire after
timeline completion, and Depth-owner changes invalidate cascade and spotlight
caches. GPU-indexed production is not inferred from CPU-recorded
`vkCmdDrawIndexedIndirectCount`. A trusted compute reduction reads the
immutable submitted count and argument buffers. It reads back one word per row
after completion, and zero counts, index counts or instance counts claim nothing.
Conditional spotlight scopes attribute classic, cell and GPU draws to the GPU
visibility word. The condition is thread-local to the spot recorder, so
concurrent cascade and pass workers never inherit it. Generation tokens keep
work from being credited to a newer replacement. Pack-only proof resources
retire when the last tracked generation returns to stock.

The device-local actual-core gate passes 92 checks with synchronization
validation and zero errors. It cataloges all 28 cell variants and initialized
GPU NEW/OLD pairs, and runs a four-path mixed frame with expected GBuffer and
shadow pixels in both recording modes. It covers hidden-work honesty, atomic
activation, compile-failure retention, ABI refusal, exact stock restoration and
retirement. Its conditional spotlight lane covers classic, cell and GPU NEW
depth owners in one GPU-conditional scope. It checks independent pixels and
per-owner attribution after a caster change. A condition-false frame is
CPU-recorded with no claimed production, and a forced redraw reports fresh
production. Eight older-host refusal checks pass. Those cataloged cell variants
are construction evidence, not per-path execution evidence. Reserved geometry
bindings, stage-specific constants, transparent/billboard owners, legal
interface widening and same-invocation attachments remain open. Both aggregate
capability flags stay false, and stages 7 and 8 still follow.


### GBuffer velocity contract on native owners — first admitted path

DX11 binds catalog t5..t9 and the b6 extras only for Lighting, Post.Tonemap,
Post.HBAO, Transparent and Atmosphere. It binds nothing of that kind for
GBuffer, Depth, Forward or Highlight. So skipping native geometry owners in
`StageBindingOwners` is parity, not a gap. DX11's real geometry reservations
are the `ANOMALY_VELOCITY` contract, applied to GBuffer permutations only
(never Depth):

- VS b6, the 240-byte `AnomalyVelocity` block;
- VS t15 previous instances and VS t16 previous bones;
- PS b7, the 16-byte probe;
- Target3 velocity, written by the same draw.

The four hash-checked Keen patch transforms now live in the shared
`GBufferVelocitySource`. DX11 calls them unchanged. Vulkan uses them with
`ANOMALY_SPIRV`, which drops DX11's internal SV_Target7 audit. Vulkan has one
register namespace per descriptor set, so those VS registers would alias
Keen's PS b6/t15 and forward t16. `Anomaly.hlsli` therefore relocates them to
set 2 under `ANOMALY_SPIRV`. As on DX11, a pack overlay of the same key wins.
A GBuffer PixelStage overlay must start from the velocity-aware source on both
hosts.

Native owner flag8 requires `VK_EXT_dynamic_rendering_unused_attachments`.
Stock pipelines keep three attachments, and only owner pipelines add Target3.
`CompileNativeOwnerGenerationWithContracts` sends the contract bit, and Anomaly
sends it only for generations that change sources. Admitted owners are the
classic GBuffer pipelines 0..19: rigid, skinned, generic-instanced, cube and
deformed-cube streams. Keen compiles those without `USE_SIMPLE_INSTANCING`,
so their `local_matrix` comes from the object block. Skinned velocity is
therefore rigid-body: bone animation is not part of DX11's classic velocity,
and the replay never covered skinned draws at all. DX11 therefore gives them
the per-draw currToPrev rows and never reads t15; only `USE_SIMPLE_INSTANCING`
(the Stage 2/GPU NEW path) indexes t15. Per-instance changes are not part of
DX11's velocity for these streams. Admitted owners compile with
`ANOMALY_VELOCITY`, leave indirect grouping, and get a set-2 layout with real
per-draw writes:

- b0 holds the replay's view-projections, the render size and the
  old-pipeline currToPrev rows, at a dynamic offset;
- b1 is the zero probe;
- t15 and t16 are bound storage buffers.

The main GBuffer scope adds the motion target only on frames with admitted
draws. The replay then loads that target and skips those draws. Without
history, owners write zero, as the replay does.

The 157-check actual-core gate (`--gbuffer-velocity-only`) passes with
synchronization validation and zero errors, on all five classic streams:

- Stock replay matches the analytic -1.067 px motion.
- Owner-emitted Target3 matches both the analytic and replay values.
- A pack-written (3, -2) reaches Target3, which the replay cannot produce.
- The skinned owner emits the analytic rigid-body motion and pack-written velocity.
- Results agree across recording modes.
- Failed compilation keeps the previous generation.
- Unregistering restores exact stock material and motion bits.
- Retirement completes.

GPU NEW GBuffer owners compile `USE_SIMPLE_INSTANCING`, which is where DX11
reads t15. One indirect draw covers many objects there, so b0 is per frame:
view-projections, size, PrevCount, HasHistory and the camera delta, with
InstanceBase 0. Vulkan's `SV_InstanceID` already indexes the global instance
buffer. The GPU adapter replaces the t15 `StructuredBuffer` with a lookup:
each instance resolves to its actor through the culled references, then reads
that actor's previous rows, which the indexed replay keeps. The replay
prepares that history once, before the GBuffer, and then skips owner batches.
Admission requires the variant's own `USE_SIMPLE_INSTANCING`. t16 has no
Vulkan consumer: GPU skinned sources keep their classic fallback, where DX11
also ignores bones. The 40-check `--gbuffer-velocity-gpu-only` gate passes with
zero errors:

- Owner motion matches the analytic -0.64 px and the indexed replay.
- Pack-written (3, -2) reaches Target3 through indirect draws.
- Results agree across recording modes.
- Unregistering restores exact stock bits.

Native-cell GBuffer owners also compile `USE_SIMPLE_INSTANCING`. They draw
each group with firstInstance 0, so b0 is per group and
`InstanceBase = history_offset / 64`, matching DX11's
`t15[SV_InstanceID + InstanceBase]`. t15 is the current slot's per-instance
history stream; its unused bytes 48..63 now carry flags.x = 1 for a known
previous world. The replay skips owner groups. The 40-check
`--gbuffer-velocity-cells-only` gate passes with zero errors: owner motion
matches the analytic -0.64 px and the cell replay, pack-written (3, -2)
reaches Target3, and unregistering restores exact stock bits.

GPU OLD owners compile Keen's classic streams, so DX11's contract for them is
the per-draw b6 currToPrev rows and `HasPrevWorld`. One indirect draw covers
many objects, so the GPU adapter renames those cbuffer members and fills
shader statics per draw instead. It computes `Prev * Invert(Curr)` from the
draw's actor history row (`draw.reserved`) and the current object rows that
the culling compute writes. The 240-byte cbuffer layout is unchanged, and the
replay skips owner batches. Admission covers classic GBuffer pipelines below
20; GPU skinned sources stay on their fallback. The 40-check
`--gbuffer-velocity-gpu-old-only` gate passes with zero errors: owner motion
matches the analytic -0.64 px and the indexed replay, pack-written (3, -2)
reaches Target3, and unregistering restores exact stock bits. The gate drives
the rigid stream; instanced and cube GPU OLD streams share the same code.

Fade-bank variants of the classic and GPU OLD streams share their base
stream's per-draw currToPrev rows; the shader applies the fade itself. Blended
static decals are also admitted. DX11 binds Target3 as an RG replace (blend
off, RG write mask) for every GBuffer draw, decals included. So Vulkan decal
owners add a fourth attachment with exactly that state, and their three
GBuffer blends are unchanged. The classic gate now covers 197 checks:

- the fade-bank owner (2562) matches its replay;
- the blended-decal owner (128) writes the analytic -1.067 px that the replay
  never covered;
- pack-written velocity and exact restoration hold on both.

Voxel GBuffer shaders (classic 20/21) are not native geometry owners on Vulkan:
they come from the voxel shader library, so packs cannot yet replace them
through native generations, and there is no voxel owner to emit velocity. DX11
gives voxels camera-only velocity from the pass-begin block. Adding voxel
owners is an owner-family gap, not velocity work.

Pack GBuffer attachments now follow DX11's same-invocation contract on these
owners. Anomaly generates `GBufferAttachmentDefs.hlsli` and
`GBufferAttachmentFields.hlsli` from the live requests, gated on
`ANOMALY_VELOCITY` so only GBuffer owners see them, as DX11 adds the macros
only to GBuffer permutations:

- `ANOMALY_ATTACH_<NAME>` and `_TARGET` with fields at SV_Target4+slot;
- packed `GBuffer1.a` via `ANOMALY_PACK_GBUFFER1A`, `ANOMALY_ATTACH_PACKED_NAME`
  and the pack's `AnomalyGBuffer1A` override.

It declares each slot's format to the host with
`sevk_declare_gbuffer_attachment`, so a pack needs no Vulkan-only replay
shader. Owner pipelines add those targets, with UNDEFINED for unused slots.
The main GBuffer scope binds declared attachment images at index 4+slot. The
replay pass loads them and skips owner draws, and decal owners write them as
replace. The 33-check `--gbuffer-attachments-only` gate passes with zero
errors:

- an R32_UINT attachment carries 777 from the owning invocation (uncovered
  pixels stay cleared);
- `GBuffer1.a` carries the pack's 0.5 (stock writes 63/255);
- velocity stays correct beside both;
- recording modes agree, and unregistering restores exact stock bits.

The Vulkan-only `GBuffer/<name>.hlsl` write shaders still serve non-owner
draws.

Native-cell, GPU NEW and GPU OLD owners write the same attachments from their
own invocations. Their draws record inside the same main GBuffer scope, so
they need no separate wiring. The velocity lanes for these paths
(`--gbuffer-velocity-cells-only`, `--gbuffer-velocity-gpu-only`,
`--gbuffer-velocity-gpu-old-only`) each add 15 attachment checks, for 55
each. On each path:

- objectid is 777 at SV_Target4;
- `GBuffer1.a` is 127 against stock 63;
- velocity stays correct;
- recording modes agree;
- unregistering restores exact GBuffer1 and motion bits.

A declared-only slot has no indexed replay shader. The indexed replay now skips
it instead of failing with "indexed attachment geometry class unavailable"
once the owning pack unregisters.

GPU OLD generic-instanced, cube and deformed-cube owners (pipelines 2, 3 and
4) are covered too. They read their instance records from the source buffer,
and their velocity comes from the same per-draw currToPrev rows as rigid
draws. The GPU OLD lane now runs each stream against its own source record
and checks that:

- velocity matches the analytic value and the stock replay (-0.64 px);
- objectid 777 and `GBuffer1.a` 127 come from the owning invocation;
- recording modes agree;
- pack-written (3, -2) velocity reaches Target3;
- unregistering restores exact stock bits.

The lane passes 140 checks, and no native change was needed.

### Transparent glass and holo owners

Stock glass and holo transparent geometry now has native owner generations,
advertised by native owner flag16 (ids 2304-2343). Owner i's VS is 2304+2*i
and its PS is 2305+2*i:

- i<10 is OIT glass pipeline i, keyed `Geometry/Passes/Transparent/*Stage.hlsli`;
- i>=10 is decal-depth glass pipeline 50+i, keyed
  `Geometry/Passes/TransparentForDecals/*Stage.hlsli`.

The catalog lists all 20 pairs, and each stock artifact compiles through its
pass `Declarations.hlsli`. The DX11 overlays, Transparent injections and
header dependencies select a pair as they do for other families.

Owners keep the stock glass descriptor contract. They also admit the pack
extras (b6) and the reserved catalog resources (t5-t9). Activation swaps the
no-cull, culled and depth-only pipelines into the stock slots. OIT glass and
the glass decal depth layers write each owner's reflected bindings, including
dynamic offsets in binding order. Native stage-binding rows admit these PS
ids, and the row cap rose from 117 to 137.

The `--transparent-owners-only` lane passes 44 checks with zero validation
errors:

- stock glass resolves to the material ColorAdd (0.25, 0.5, 0.75);
- an owner that swizzles its OIT color gives (0.75, 0.5, 0.25);
- scaling by a catalog SRV (t7) and the b6 render size gives the same at
  0.25, and doubles when the catalog value doubles;
- the pair executes in the current frame, and never in frames without glass;
- a failed compile keeps the previous pair;
- a TransparentForDecals owner that discards every depth fragment removes
  the glass surface, so a lit transparent decal (1.0586) composites to
  exactly zero;
- recording modes agree, and unregistering restores exact stock bits.

Billboards (`Transparent/Billboards.hlsl`, stock ids 20-51 and LDR 100-131)
have no owner family yet.

Still open on this contract:

- voxel owners (not yet native geometry owners);
- GPU OLD instanced/cube execution fixtures;
- probe modes;
- attachment fixtures on native-cell/GPU owners (they share the format helper).

Both aggregate capability flags stay false.
