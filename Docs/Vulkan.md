# Vulkan guest bus

Anomaly is the pack host on Keen DX11 and on SE-Vulkan. Packs keep `Fullscreen/<Slot>`, `anomaly.json`, `SetUniforms`, and catalog names. The backend is chosen once, when the operational renderer exists. A missing SE-Vulkan assembly at plugin init stays pending until `MyProgram.m_renderer` is `MyDX11Render`. `RequestedBackend` is not the session. A second, different choice is recorded and does not switch the loaded assembly. The other path stays unloaded.

## Assemblies

| Assembly | When it loads | What it owns |
|---|---|---|
| `Anomaly.dll` | Always. This is the Pulsar plugin. | Registries, pack scan, terminal pages, the HLSL bus, and the settings dialog (`Anomaly.cfg`). No `VRage.Render11`, no SharpDX, no Keen patches. |
| `Anomaly.Dx11.dll` | Only after the live renderer is `MyDX11Render`. Beside the plugin, or the plugin's `ClientPlugin\bin` build when Pulsar source mode compiled the core into `DevFolder`. Not an `IPlugin`, and not a second Pulsar plugin. | Today's Harmony patches, SharpDX compiles, and `MyManagers` textures. |

Pulsar source mode compiles every `.cs` file under `Anomaly.xml` `SourceDirectories` into `Anomaly.dll`. Those directories are `ClientPlugin/Backend` and `ClientPlugin/Settings`. `Settings` is where Pulsar reads `[assembly: AssemblyVersion]` and where `Anomaly.cfg` loads, including on Vulkan. The DX11 sources stay in the rest of `ClientPlugin` and are built by MSBuild as `Anomaly.Dx11.dll`. `Settings` is compiled into that assembly too. The two copies are different types and cannot share one Pulsar compilation. On DX11 the device patches read the DX11 `Config`; the Pulsar dialog edits that copy once `Anomaly.Dx11` is loaded. A Vulkan recovery launch is that DX11 path. Source mode's `DevFolder\...\Bin` does not contain the satellite, so the lookup also checks the plugin's `ClientPlugin\bin` output for the running runtime and then Pulsar `Local`. The same lookup loads `SpaceEngineersDLSS.Dx11.dll`, `SpaceEngineersFRS.Dx11.dll`, `SpaceEngineersFrameGen.Dx11.dll`, and `HdrOutput.Dx11.dll`. If the satellite is still missing, Anomaly opens the core settings dialog and the DLSS/FRS anti-aliasing lists keep their fallback items.

SE-Vulkan's compatibility check still fail-closes on a direct `VRage.Render11` or `SharpDX*` reference. Anomaly is not on the known-consumer list. An older Anomaly build that still references those assemblies still forces DX11. SE-DLSS, SE-FRS, SE-FG, and HdrRender keep those references in a sibling `*.Dx11.dll` that loads only on Windows DX11. Their main assemblies request renderer services on Vulkan: DLSS asks the renderer to evaluate driver NGX when `nvngx_dlss.dll` is beside the plugin, FRS asks the renderer to evaluate FSR 3.1.4 when `amd_fidelityfx_vk.dll` is beside the plugin, frame generation asks the renderer to run the same bidirectional warp as DX11 and present that frame immediately, and HDR asks for an FP16 swapchain. The temporal upscale is the Catmull-Rom fallback. It is not NGX and not FSR. DX11 FRS stays on FSR 2.2.1. FSR 4 is not used; that SDK dropped Vulkan. DLSS headers stay out of git; the renderer binds `NVSDK_NGX_VULKAN_*` from the driver's `_nvngx.dll` the same way the DX11 plugin binds `NVSDK_NGX_D3D11_*`. Aurora, Volumetric Clouds, Screen Space Shadows, Final Frontier, and Prism.SSGI allocate through the catalog. Prism's DX11 SVGF stack is `Prism.SSGI.Dx11`. On Vulkan the same temporal, moments, and à-trous passes run on the guest bus and DirectAdd into world HDR. SE-RT's software ray backend is `SERT.Dx11`, loaded only on Windows DX11. Its hardware path is the renderer ray-query export.

Linux loads a pack only when Anomaly has opened the Vulkan guest bus. DX11 device work, Harmony against `MyRender11`, and SharpDX stay closed. A Linux session whose route is still pending or DX11 stays idle.

`IgnoresAccessChecksTo("VRage.Render11")` is a string attribute. It is not a reference.

## Renderer route

Packs read `ClientPlugin.Backend.RendererRoute` by reflection. It is the operational renderer, and it is readable before that choice exists.

| `Active` | Name | Shared bus | DX11 device | Vulkan guest |
|---|---|---|---|---|
| 0 | pending | open | closed | closed |
| 1 | DX11 | open | open | closed |
| 2 | Vulkan | open | closed | open |

`UseSharedBus`, `UseDx11Device`, and `UseGuestImages` are the path flags. `IsPathOpen` takes `shared`, `dx11-device`, or `guest`. `Decided` is false while `Active` is 0. `LastRefusal` records a blocked device call. A DX11 call such as `DrawFullscreen`, `PublishedBuffer.Srv`, `OwnedPassRegistry` device work, `FrameTemporal`, the celestial registry, or the shared-volume registry does not run on Vulkan or while the choice is pending. `ShaderPackRegistry.Register`, `OwnedPassRegistry.Register` / `Unregister`, and `FullscreenPassRegistry.SetEnabled` issued from `LoadAssets` are held and applied when DX11 commits, after the json programs exist. A Replace left at its json default until that checkbox lands overwrites the lit scene. Vulkan discards the DX11 hold. A guest image is not allocated on DX11. Catalog `PublishRgba8` and volume uploads stay on the shared bus: they keep the bytes and bind the device that wins. Do not read SE-Vulkan `RequestedBackend` or `BackendRuntime` to choose a path.

Packs copy `AnomalyRenderer` (same reflection body, the pack's own namespace). It reads `RendererRoute` with no compile reference to Anomaly. `Register`, `SetUniforms`, and `PublishRgba8` stay on the shared bus. Device work waits:

```csharp
if (!AnomalyRenderer.Decided)
    return;

if (AnomalyRenderer.UseDx11Device)
{
    // Rc, Srv, OwnedPassRegistry, FrameTemporal, celestial draws
}
else if (AnomalyRenderer.UseGuestImages)
{
    // catalog images and fullscreen packets
}
```

## Guest packet

Before the frame ends, Anomaly submits copied pass packets: slot, SPIR-V, reflected bindings, uniform bytes, sampled and storage image ids, compose mode. Native replays them on the render thread. Managed code does not record a `VkCommandBuffer` and does not touch `MyRender11.DeviceInstance` during a Vulkan session.

ABI stays version 1. The guest exports and `SevkGuestPass` (416 bytes, natural alignment) are additive. Register bands `b/t/s/u` at 0/128/256/384 match the fullscreen bus (`t0–t13`, `b6` extras, `b7` uniforms).

A pipeline is created for the fixed bus: `t0–t13`, `s0–s2`, `b6` extras (320 B), `b7` uniforms (256 B), owned `b0` (at most 256 B), storage buffers at `t1` / `t10` / `t11`, and compute `u0`. A binding outside that set returns no pipeline and the pass is skipped. An empty packet records no draws. `guest_replay` still runs at the six marks.

IsolatedAdd, IsolatedMix, IsolatedSub, DirectAdd, Chain, and Replace composite onto the world HDR target. PublishOnly and compute write the named catalog image and `pass.{id}`. The world target is not cleared. `passes[].scale` and `SetScale` snap to 1, 0.5, or 0.25 on Vulkan the same way they do on DX11. `passes[].history` ping-pongs two images and catalogs the previous one under that name before the pass draws. Sampled ids in the top of the `uint64` range are renderer images, not guest allocations: HDR, depth, the three GBuffer colors, the upscale image, and the display image. `sevk_get_guest_frame` is 344 bytes. The first 328 bytes are the sun, view, and resolution the extras constant buffer uses. The tail is `far_plane`, `output_width`, `output_height`, and `service_flags`. `FrameTemporal.FarClip` reads the far plane. `AnomalyCameraToWorldPad.x` carries the same far value inside the 320-byte extras buffer. `DisplayServices.Request` sets service bits without clearing the others: 1 HDR swapchain, 2 the bidirectional frame-generation warp plus its extra immediate present, 4 the renderer temporal upscale. Bit 8 is set in the returned flags when the surface actually opened an FP16 extended-sRGB swapchain.

| Mark | Value | Where |
|---|---|---|
| AfterLighting | 1 | After deferred lighting, before the environment |
| AfterAtmosphere | 2 | After the environment, before flares |
| AfterTransparent | 3 | After transparency, before the HDR-to-SRV barrier |
| BeforeTonemap | 4 | Start of postprocess |
| AfterTonemap | 5 | After the tone stage, or immediately when post is disabled |
| AfterUpscale | 6 | After world compose, before HUD sprites |

C# owned-pass slots are 0–5. The native mark is the value in this table, chosen by slot name. Do not pass the C# enum ordinal as the mark.

Compose values: IsolatedAdd 0, IsolatedMix 1, IsolatedSub 2, Chain 3, Replace 4, PublishOnly 5, DirectAdd 6.

## Catalog images

`PublishedBuffer.Publish(object, IntPtr, int, int)` stays as it is. Both the DX11 type and the core facade add `PublishRgba8`, `PublishVolumeRgba8`, `PublishStructuredFloats`, and `PublishVolumeMips`. `BufferCatalog.Active(name)` stays `object` plus `IntPtr`. On DX11 the active value is the inner buffer, so `.Srv` still exists.

Owned passes that the bus samples — linear depth, Hi-Z, history color, reactive mask, lit mips, and camera velocity — publish under the same catalog names. On Vulkan those are fullscreen programs (`CameraVelocity.hlsl`, `LinearDepth.hlsl`, `HiZDownsample.hlsl`, `HistoryCopy.hlsl`, `ReactiveClear.hlsl`). On DX11 the existing owned-buffer pass still publishes them. Vulkan object motion is an RG16F image written by a depth-equal geometry pass (rigid, generic, cube, and deformed cube). `CameraVelocity.hlsl` keeps those pixels and fills the rest from depth. Catalog `velocity` is the `CameraVelocity` result at guest image `0xFFFF00000000000A`: object motion where the geometry pass wrote it, camera reprojection from depth everywhere else. Frame generation samples that image when `history_valid` is set, with `mv = output / velocity size`, and seeds instead of warping when `FrameTemporal.InvalidateHistory` clears history. Without the image it keeps the renderer object-motion buffer. `volumeReactive` is guest image `0xFFFF00000000000B` when a program publishes that catalog name; the warp uses it as the DX11 reject mask. The shared froxel renderer that writes this mask still runs on DX11 only.

Cloud shape and detail volumes are 128³ and 64³ with a CPU box-filter mip chain. `PublishVolumeMips` uploads that chain; the guest image keeps every mip. Weather is 64×64×1. Storms are 128 floats at structured stride 16 and stay a structured buffer, which this bus does not sample.

Catalog `hdrColor`, `upscaledColor`, and `displayColor` are renderer images. `ClaimUpscale` records the pack and tells the renderer the owner. Bilinear compose stays the default until that owner evaluates. AfterTonemap (mark 5) samples and merges into the tonemap image, the same LDR result DX11 passes to SE-DLSS after Anomaly's postfix. When the owner is `se-dlss`, the renderer evaluates that image there and publishes the dest as `upscaledColor`. AfterUpscale (mark 6) then samples and merges into that dest, before compose. Vendor SDK calls stay in HdrRender, DLSS, FSR, and frame generation. `pointShadowAtlas` is not published: the renderer has no local-character cube to alias.

`RequestSrv` on Vulkan fills `t7–t9` and `t12–t13` from catalog names. After tonemap, `t4–t6` are `avgLuminance`, `bloom`, and `dirt` when those catalog images exist.

`VolumetricMediumRegistry.Register` on Vulkan compiles the provider into one compute packet at AfterAtmosphere. The three catalog volumes, including their mip chain, are `t11–t13`. A following IsolatedAdd samples that image. The DX11 froxel stack uses extra UAVs and a provider constant buffer that do not fit this bus, so the guest march is 12 bounded steps of `EvaluateMedium`. `CelestialBackgroundRegistry.Register` is a depth-tested Replace at AfterLighting. `SetNativeSunGlare(id, false)` drops stock directional glare once that draw has a pipeline. Art is a 2D array image. Structured celestial data is a storage buffer at `t1`.

## Status

Show Status and Velocity Status keep the DX11 section labels when the DX11 report is not loaded. The guest bus fills compile, shader packs, attachments, pass binds, owned programs, the color bus, fullscreen programs, owned buffers, the motion image, and the frame. Velocity Status adds the selected debug view, probe, checkpoint, and the audit block while a probe, checkpoint, or Pipeline Audit is on.

## Still later

Stock GBuffer color targets stay three plus depth. A pack attachment is a separate image, written by `GBuffer/<name>.hlsl` in a depth-equal pass. Catalog `Active(name)` is sentinel `0xFFFF000000000009` plus the slot (`SV_Target4` is slot 0). Object motion is the renderer RG16F pass described above, not a guest fullscreen program. Do not widen Keen's DX11 GBuffer, add a second compile hook, or move vendor upscalers into Anomaly. SE-RT's software DX11 backend is unchanged. Its Vulkan hardware path is a native ray-query pass on the renderer device, not this fullscreen guest bus.

## Acceptance

`Tests/GuestContract` parses one `anomaly.json` and one `Fullscreen/AfterLighting` program into the shared pass list both backends use. SE-Vulkan's guest draw fixture replays a descriptor-free IsolatedAdd and a `t0`/`s0` bus shader at mark 1, both validation-clean, and a `t20` shader is skipped. It exits cleanly when the native library, DXC, or a GPU is missing. DX11 screenshots stay the visual reference.
