# Anomaly Shader Framework

Pulsar client plugin that intercepts Space Engineers 1’s DX11 GBuffer and publishes shared GPU buffers. First product: a real velocity buffer (object motion plus camera fallback). First consumer: [SE-DLSS](https://github.com/PhoenixTheSage/SE-DLSS).

This is not a graphics preset. Other plugins bind `IVelocityBuffer` by well-known type name; see [ClientPlugin/Velocity/README.md](ClientPlugin/Velocity/README.md).

Architecture supports [Rich HUD Framework](https://github.com/DarkHelmet/RichHudFramework) coexistence (Anomaly must not leave RT/SRV bound).

## Requirements

- Space Engineers with [Pulsar](https://github.com/SpaceGT/Pulsar), Windows
- NVIDIA RTX is **not** required for Anomaly. Consumers such as SE-DLSS have their own GPU requirements.

## Settings

Plugin config:

- **Velocity source** — `GBuffer` (target piggyback), `OwnedRaster` (bootstrap only), or `CameraOnly`
- **Show Status** — compile intercept, camera pass, velocity source, buffer size, `HistoryValid`

Compile intercept (Slice A) is live: Keen permutations get `ANOMALY=1` and Anomaly’s include directory. Camera velocity (Slice B) writes an `RG16F` buffer at internal resolution after GBuffer when the source is `CameraOnly`. Actor history (Slice C) snapshots absolute world matrices by ActorID. GBuffer piggyback (Slice D) overlays `Geometry/Passes/GBuffer` + `GBufferWrite.hlsli`, binds a fourth `RG16F` target on GBuffer only (`ANOMALY_VELOCITY`), and packs previous worlds in Stage 2 instance order. Slice E fills coverage holes: previous bones on t16 (count mismatch → camera term), clipmap cells stay camera-only, and a composite pass keeps GBuffer MVs on geometry while filling sky/particles/foliage from depth so the buffer is never left at clear-zero. Slice F is the pack registry: named Pulsar assets (`AssetFolder` + `Shaders`), `ClientPlugin.Shaders.ShaderPackRegistry.Register`, Keen-relative `Overlay/` (fail closed on conflict), additive `Inject/`, and a developer-only local drop under Pulsar `Data/Anomaly/Packs`. Default velocity source is `GBuffer`.

Docs: [implementation roadmap](Docs/ROADMAP.md) · [shader API](Docs/ShaderAPI.md) · [shader packs](Docs/ShaderPacks.md) · [product plan](Docs/PLAN.md) · [Keen shaders](Docs/KeenShaders.md).

## Building

- .NET Framework 4.8.1 targeting pack and .NET 10 SDK
- Build `ClientPlugin` (deploys to Pulsar `Legacy\Local` or `Interim\Local`; close the game if the DLL is in use)

GPU work uses Keen / SharpDX D3D11 from C#. There is no NGX native wrapper in this repo.

Debug with Pulsar `Legacy.exe` / `Interim.exe` and `-sources`.

## Known interactions

[SmoothFrames](https://github.com/WhiteFang34/SmoothFrames) also patches the render thread. Jitter plus camera interpolation can interact once intercepts exist.

## Bug reports

Open an issue with **Show Status** text, GPU, driver version, and `SpaceEngineers.log`.
