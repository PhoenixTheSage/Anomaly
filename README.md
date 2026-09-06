# Anomaly Shader Framework

Pulsar client plugin that intercepts Space Engineers 1’s DX11 GBuffer and publishes shared GPU buffers. First product: a real velocity buffer (object motion plus camera fallback). First consumer: [SE-DLSS](https://github.com/PhoenixTheSage/SE-DLSS).

This is not a graphics preset. It does not change the picture by itself. Other plugins bind `IVelocityBuffer` by well-known type name; see [ClientPlugin/Velocity/README.md](ClientPlugin/Velocity/README.md).

Architecture supports [Rich HUD Framework](https://github.com/ZachHembree/RichHudFramework.Client) coexistence (Anomaly must not leave RT/SRV bound). The [client + Shared modules](ClientPlugin/RichHudFramework/VENDOR.md) are vendored; [Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081) is an optional world mod, not a Pulsar `DependencyId`. Settings stay on the Pulsar MyGui dialog until a terminal page is added.

## Install

- Space Engineers with [Pulsar](https://github.com/SpaceGT/Pulsar), Windows
- Enable **Anomaly Shader Framework** from PluginHub, or locally from this repo
- Enable it whenever a consumer plugin (SE-DLSS, a shader pack) lists it as a dependency

NVIDIA RTX is **not** required for Anomaly. Consumers such as SE-DLSS have their own GPU requirements.

## Settings

- **Velocity source** — `GBuffer` (object motion on Keen geometry pixels) or `CameraOnly` (fullscreen depth reprojection)
- **Debug velocity** — legacy toggle for the velocity overlay (same as Debug buffer = Velocity)
- **Debug buffer** — fullscreen overlay of a catalog texture (`Off`, `Velocity`, `GBufferVelocityRaw`, `VelocityPipelineAudit`, `LinearDepth`, `HistoryColor`, `HiZ`, `ReactiveMask`, `FullscreenIsolated`). `Velocity` is the final camera-filled product; `GBufferVelocityRaw` shows `SV_Target3` before that fallback. `VelocityPipelineAudit` repeats the scene in six panels from one Keen geometry draw. Top row: Target3, pixel execution (green=true, dark red=false), PS b7 (magenta=true). Bottom row: untouched VS→PS velocity, GBuffer0 b7 marker (green=true, dark red=false), raw GBuffer0. Drawn after the scene copy at the presented size (covers DLSS/DRS output). Velocity: complementary-depth ≈ 0 (sky) is dark grey so still meshes silhouette; mid-gray is no motion on geometry; red/green are signed X/Y pixel delta; blue is speed; magenta is `HistoryValid` false on geometry. Linear depth / Hi-Z: log grayscale. History color: previous HDR lighting buffer. Reactive mask: white = do not trust history. Fullscreen isolated: last pack `Fullscreen/` program output. HUD still draws on top.
- **Debug scale (px)** — pixel motion that maps to full color for the velocity overlay (1–128, default 32). Lower is more sensitive.
- **Velocity probe** — developer-only GBuffer diagnostics. `TargetClear` puts a known +X value in the raw target before geometry; `MrtWrite` clears to -X, selects +X in the active velocity VS and final GBuffer PS output, repeats the four-target/constant-buffer binds, and repairs Target3 blend masks at the exact draw boundary (pink = write, cyan = no write, gray = explicit zero); `HistoryCoverage` emits +X for a previous-world hit and +Y for a miss; `PassEndClear` clears the final Target3 once on Keen's immediate context after every deferred geometry list executes. Every probe changes immediately without rebuilding shaders. Return it to `Off` after testing.
- **Target3 checkpoint** — developer-only source selector for `GBufferVelocityRaw` and the Target3 panel of `VelocityPipelineAudit`. `Live` displays the final raw target. The other choices GPU-copy exactly one selected scheduler boundary (clear, old geometry, Stage 2, decals, resolver, transparent, GBuffer teardown, or scheduler end) into an isolated snapshot. All hooks ship in one build, but only the selected checkpoint copies; the published velocity path always uses the live target.
- **Show Status** — concise operational health for shader integration, Rich HUD, passes, buffers, velocity, active debug modes, buffer size, and `HistoryValid`
- **Debug Status** — detailed compile, bytecode, GBuffer, Stage 2, MRT, history, shader-object, and draw-boundary diagnostics for development and bug reports

Default velocity source is `GBuffer`. Compile intercept is live: Keen permutations get `ANOMALY=1` and Anomaly’s include directory. Camera velocity writes an `RG16F` buffer at internal resolution after GBuffer. Actor history snapshots world matrices by ActorID. GBuffer piggyback overlays `Geometry/Passes/GBuffer` + `GBufferWrite.hlsli`, binds a fourth `RG16F` target on GBuffer only (`ANOMALY_VELOCITY`), and packs previous worlds in Stage 2 instance order. A composite pass keeps GBuffer MVs on geometry while filling sky/particles/foliage from depth. Velocity + `linearDepth` / `hiZ` freeze at `MyRenderScheduler.Done` (before atmosphere / transparent). `OwnedPassRegistry` Harmony-owns AfterLighting / AfterAtmosphere / AfterTransparent / BeforeTonemap / AfterTonemap / AfterUpscale (`NotifyUpscaleComplete`). `FullscreenPassRegistry` compiles pack `Fullscreen/<Slot>/*.hlsl` and `passes[]` (Anomaly owns the draw; IsolatedAdd default; Replace fail closed). `FrameTemporal` reads SE-DLSS jitter and republishes an unjittered VP on the extras CB. Atmosphere wrap binds extras at **t6** (Keen `DensityLut` stays t5). `reactiveMask` is published when a pass sets `TemporalPolicy.Reactive`. Pack registry: named Pulsar assets (`AssetFolder` + `Shaders`), `ClientPlugin.Shaders.ShaderPackRegistry.Register`, Keen-relative `Overlay/` (fail closed on conflict), stage-scoped `Inject/` (`Anomaly/Extras/<Stage>.hlsli`, including Lighting and Atmosphere extras), pack `defines` on GBuffer, lighting, and atmosphere, `GBufferAttachments.Request` extra MRTs, named stages via `ShaderStages` (including Shadows, Atmosphere, Decals, GPUParticles, EnvProbe, Foliage), `ShaderBindRegistry` (lighting/post/atmosphere SRVs, unbind after), `BufferCatalog.Active` / `Publish` / `RegisterLifetime` for velocity / linear depth / Hi-Z / history / reactive mask, and a developer-only local drop under Pulsar `Data/Anomaly/Packs`. After apply, Anomaly compiles a sentinel for each live named stage and rolls back a pack that breaks it; compile errors log the pack id. Overlay of Anomaly’s GBuffer **write** stages requires `exclusive: ["GBuffer"]`; read wraps accept GBuffer or Lighting; `Lighting/Light.hlsli` requires `exclusive: ["Lighting"]`; AtmosphereCommon wrap requires `exclusive: ["Atmosphere"]`.

Docs: [shader developer wiki](https://github.com/PhoenixTheSage/Anomaly/wiki) · [implementation roadmap](Docs/ROADMAP.md) · [extensibility](Docs/Extensibility.md) · [shader API](Docs/ShaderAPI.md) · [shader packs](Docs/ShaderPacks.md) · [product plan](Docs/PLAN.md) · [Keen shaders](Docs/KeenShaders.md).

## Building

- .NET Framework 4.8.1 targeting pack and .NET 10 SDK
- Build `ClientPlugin` (deploys to Pulsar `Legacy\Local` or `Interim\Local`; close the game if the DLL is in use)
- PluginHub compiles from GitHub source (this repo’s `ClientPlugin` tree + `Assets`). Confirm a Pulsar **dev folder** / `-sources` build before pinning a commit. Pulsar’s Roslyn publicizer does not expose virtual game methods — Harmony targets use string names, not `nameof`.

GPU work uses Keen / SharpDX D3D11 from C#. There is no NGX native wrapper in this repo.

Debug with Pulsar `Legacy.exe` / `Interim.exe` and `-sources`.

## Known interactions

[Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081) is optional. Show Status reports `Rich HUD: registered` when the handshake succeeds in a world that has Master enabled; otherwise it stays `waiting` / `idle` and MyGui settings still work.

[SmoothFrames](https://github.com/WhiteFang34/SmoothFrames) also patches the render thread. Jitter plus camera interpolation can interact once intercepts exist.

## Bug reports

Open an issue with **Show Status** and **Debug Status** text, GPU, driver version, and `SpaceEngineers.log`.
