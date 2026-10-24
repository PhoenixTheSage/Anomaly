# RayScene API v1 and opaque lighting ownership

Resolve `ClientPlugin.Shaders.RayScene` and `ClientPlugin.Shaders.OpaqueLightingOwnership` by name. Tenant plugins need no compile-time Anomaly reference and must not patch Keen lighting passes themselves.

`Register(string id, int apiVersion, Action<object[]> frame)` requests capture and installs a callback at `AfterLighting / BeforeFullscreen`. Register at plugin initialization so capture precedes world uploads. `Unregister(id)` releases the callback and ownership. `RegisterCompleted(id, Action<object>)` supplies an immediate `DeviceContext` at scheduler completion for nonblocking query polling; `null` signals world/device reset. `CompileShader(path, profile)` uses the game's include/cache machinery: profile 0 VS, 1 PS, 2 GS, 3 CS.

`SetFrameEnabled(id, enabled)` pauses frame enumeration and texture lookup for a disabled tenant while keeping upload capture armed. Registration starts enabled; SE-RT sets this to match its configuration. Reset/completion callbacks continue so resource retirement can finish. `SetFarSunEnabled(id, enabled)` optionally requests the shared far-only sunlight product for an enabled tenant; an old framework can omit this method/product without breaking v1.

Callback resources are borrowed, valid only during that callback. Record commands on **the supplied context**. Never dispose engine resources, use an immediate context from a deferred worker, or retain borrowed resources past reset. The bridge clears Keen's context state before tenant recording and restores native frame/sampler bindings afterward. Generic instance buffers and unsupported techniques are coverage gaps, not empty space. CPU mesh captures survive device reset and clear on world unload. Shader compilation produces optimized bytecode and can run on a worker.

## Array ABI

`frame` fields, append-only within v1:

0. API version (`int`).
1. world/device epoch (`int`).
2. frame index (`uint`).
3. absolute camera XYZ (`double[3]`).
4. mesh payloads (`object[][]`). A mesh is `{ revision int, vertices float[] stride 16, indices int[], parts object[][], isVoxel bool }`. Vertex fields are position XYZ, normal XYZ, UV, bone indices XYZW, bone weights XYZW. Voxel UV stores the first two material blend weights.
5. instances (`object[][]`): `{ actor uint, mesh index int (-1 = unknown), camera-relative row-major world float[16], variants object[], bounds float[6] }`. A variant is `{ part, color/emissivity float[4], borrowed texture views object[], bone matrices float[], optional optical descriptor object[] }`.
6. native light records (`float[]`, stride 16): camera-relative position XYZ, range, **already intensity-scaled linear color RGB**, original falloff, world forward XYZ, aperture cosine, kind (point 0 / spot 1), actor ID **bitcast** to float, diffuse factor, gloss factor. Reinterpret ID bits; do not convert its numeric float value.
7. borrowed GPU objects: device, supplied context, HDR render target, depth SRV, GBuffer0/1/2 SRVs, velocity SRV (nullable), native frame CB, main-view sun shadow SRV, close/far environment probe SRVs, ambient BRDF LUT SRV. Velocity is backward motion in full-resolution pixels.
8. render width/height (`int[2]`).
9. sun-toward XYZ / diffuse factor, sun RGB / history-valid, sky ambient RGB / native bloom emissiveness (`float[12]`). The final value scales mesh/voxel emitted radiance in native HDR units; it occupies the previously reserved field.
10. unjittered view-projection (`float[16]`).
11. previous unjittered view-projection (`float[16]`).
12. emitter proxy records (`object[][]`), described below.
13. native light texture records (`object[][]`), aligned with field 6: `{ spotlight borrowed texture SRV (nullable), world up float[3] }`. Point entries have no texture.
14. physical native-light origins (`float[]`, stride 3), aligned with field 6: camera-relative light actor translation XYZ before `PointOffset`. Point and spot entries are both present. Field 6 preserves the original native offset position for compatibility; tenants choose their source convention without modifying borrowed arrays. A tenant requiring physical positions must fail closed if this appended channel is absent or malformed.

15. Optional far-sun product (`object[]`): `{ version int (1), current frame uint, borrowed depth SRV (nullable), camera-relative row-major world-to-clip float[16], latest GPU milliseconds double, completed measurements long, status string }`. A null view means unavailable; the frame tag must match. Convert clip XY to UV with `(x * .5 + .5, .5 - y * .5)` after dividing by W; Z is native 0-1 depth. The 2048-square R32 product covers a 256 m receiver region with 32 km caster reach. It excludes fragments within the camera's 256 m resident sphere. Native depth-material alpha cutouts/dither are preserved. Anomaly owns culling, isolated scratch views, asynchronous variant compilation, b13 clipping constants, validity, timing and lifecycle. Unknown/unready shader variants invalidate publication; retain native distant shadows. Local ray-traced sun visibility multiplies this far-only coverage, without multiplying the local-containing native mask again.

An optical descriptor at variant[4] is optional: `{ version int (1), unpremultiplied linear tint RGB / opacity float[4], borrowed color/alpha texture SRV, UV offset XY / scale XY float[4], optical revision uint, front-face-only bool, category string ("thin-glass") }`. Only known GLASS techniques resolved through the native transparent registry emit it; the error material, unresolved textures and unsupported categories do not. Revisions, texture replacement and sidedness changes invalidate affected histories/atlas versions. Tint is an artistic approximation, not measured absorption. Camera-visible transparency stays native. Old four-field variants remain valid coverage snapshots and cannot certify transparent transmission.

SE-RT primary confirmation passes supported glass to match the opaque GBuffer receiver. Continuation through refractive glass remains unsupported and conservative; primary/secondary NEE and sun use bounded thin transmission. Unsupported transparent categories remain coverage gaps even for primary confirmation.
A part is `{ first index int, index count int, base vertex int, material name string, texture paths string[], technique string }`. Mesh textures are color/metal, normal/gloss, extension, alpha mask. Voxel parts append reciprocal near tiling scales `float[3]` at index 6; their paths/views have 24 entries: material 0 XZ/Y, material 1 XZ/Y, material 2 XZ/Y, each with four standard texture slots. Absent triple materials repeat material 0. Near textures are an approximation for secondary transport, not the complete native voxel shader.

`SetEmitter(uint id, float[16] cameraRelativeDisc, object borrowedTexture, uint nativeLightOwner, uint sourceRevision)` / `RemoveEmitter(id)` support effect producers. Nonzero ID is stable per source lifetime. Disc fields: center XYZ, radius, emitted linear RGB, reserved, normal XYZ, area, four reserved. Use `uint.MaxValue` for no analytic owner; a matching native light ID suppresses the proxy. Re-submit camera-relative positions each frame. Built-in file-texture, one-frame and persistent billboards provide bounded disk proxies; render-target billboards currently use color proxies. CPU metadata from active GPU emitters also supplies approximate effect sources, with key-averaged radiance, centroid/radius and atlas textures. Individual GPU particle buffers are not decoded. These are lighting sources, not visibility geometry.

## Vulkan route

On Vulkan the same v1 frame is delivered before guest submission each frame. Fields keep their DX11 meaning, with these differences:

- **Field 7** keeps DX11's positions, but entries are renderer ids, not views: `{ "vulkan", tenant session, HDR target, depth, GBuffer0/1/2, velocity (0 when unpublished), frame constants, sun shadows, close environment cube, far environment cube, ambient BRDF }`.
- **The session** (`IReadOnlyDictionary<string, Delegate>`, SE-Vulkan `sevk_tenant_*`) replaces the device context. It holds BCL delegates only:
  - `ApiVersion` → 1
  - `CreateBuffer(bytes)` → id
  - `CreateImage(width, height, layers, VkFormat, flags)` → id; flag 1 = array view
  - `CreateCompute(spirv, entry)`
  - `CreateRaster(vsSpirv, vsEntry, psSpirv, psEntry, formats)`: a fullscreen triangle from three `SV_VertexID` vertices; format 0 = renderer HDR
  - `Destroy(id)`
  - `Record(commands, bindings, blob)`
  - `Readback(id, layer, offset, bytes)` (diagnostics)
  - `Stats()`
- **Commands and bindings** are the 128-byte `SevkTenantCommand` and 32-byte `SevkTenantBinding` records documented in `se_vulkan.h`. Commands: dispatch, fullscreen MRT draw, fill, clear, copy, upload. Bindings use guest register bands (b → 0, t → 128, s → 256, u → 384).
- **Ray query** (devices with ray query; the session's `RayQuery` reports it):
  - `CreateAccel(top)` creates an acceleration-structure id.
  - Command 9 builds a BLAS from a tenant vertex buffer and a uint32 index range, or from 24-byte AABBs.
  - Command 10 builds a TLAS from 80-byte instance records: `VkTransformMatrixKHR` rows, custom index, mask, flags, BLAS id.
  - Binding kind 8 binds a TLAS. An unbound or unbuilt one is an empty TLAS.
  - Geometry is non-opaque, so `RayQuery<RAY_FLAG_FORCE_NON_OPAQUE>` sees every candidate and the tenant's material rules decide each one.
- **Unbound or null bindings** read zero, like DX11 null views.
- **Image formats:** storage images bind any compatible format; SPIR-V storage formats are retyped Unknown.
- **When the list runs:** after deferred lighting, before AfterLighting guest passes, where DX11 composes into LBuffer. HDR is the only renderer draw target. Sampling HDR reads that frame's snapshot.
- **Resources:** `Destroy` retires a resource once the GPU is done with it, so tenants need no retirement query. A new session object means a new renderer; ids from an old session are gone.
- **`CompileShader(path, profile)`** returns guest-band SPIR-V for profiles 0, 1 and 3, using the stock entry names. Profile 4 (Vulkan only) compiles compute with inline ray tracing (`cs_6_5`, `SPV_KHR_ray_query`). The game shader folder is the first include root, so a tenant's own `Common.hlsli` cannot shadow Keen's.
- **`RegisterCompleted`** callbacks run once per frame before delivery, with the tenant session as the context. The session's `Timing()` returns `{GPU milliseconds, measurements}`. That is the GPU time of the latest completed tenant list, from timestamps around it, typically one or two frames old; it is NaN and 0 without timestamps. Adaptive budgets poll it as DX11 tenants poll their timer queries.
- **Field 15** (far sun) is null until the Vulkan far-sun pass exists.
- **Capture gaps** (absent or unknown coverage boxes): skinned and instanced actors, voxels, and billboard/GPU emitters.

## Composition transaction

Scene residency includes nearby off-screen actors. `IMyActor.IsVisible` is the explicit actor visibility flag, not frustum visibility, so explicitly hidden previews are excluded. The local-character exception remains. Mesh coverage is bounded to the 256 m camera-relative residency sphere; unsupported or missing meshes stay coverage boxes.

Claim `Direct=1`, `DiffuseIndirect=2`, `SpecularIndirect=4` with `Claim(id, signals)` before rendering. Conflicting claims fail closed. Call `Commit(id)` immediately before a successful masked opaque HDR replacement; stop if it fails. `Release(id)` on disable, exception and unregister. Frame start clears the committed owner. The native opaque result must already exist and invalid pixels must preserve it.

Other tenants declare `RegisterContribution(programId, signals)`. Both owned passes and fullscreen programs consult this registry. Compatibility aliases cover `prism.ssgi.*` and `screenspace.shadows.*`. Suppression is for the frame; it does not disable the other plugin's settings or resource lifetime. Native transparency, fog/atmosphere order and postprocessing remain the renderer's responsibility.

The first tenant is SE-RT. Its runtime builds, WARP tests and remaining acceptance conditions are documented in `SE-RT/Docs/Implementation.md`. ABI compatibility is validated in the local build; live worlds and renderer updates still require a smoke test.
