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
- **Show Status** — source, whether GBuffer injection is live, history actor count, buffer size

The current tree is a compile-and-load stub: Harmony + publicizer + `VRage.Render11` + the velocity registry. Shader intercepts and the velocity RT are later plan phases ([Docs/PLAN.md](Docs/PLAN.md)).

## Building

- .NET Framework 4.8.1 targeting pack and .NET 10 SDK
- Build `ClientPlugin` (deploys to Pulsar `Legacy\Local` or `Interim\Local`; close the game if the DLL is in use)

GPU work uses Keen / SharpDX D3D11 from C#. There is no NGX native wrapper in this repo.

Debug with Pulsar `Legacy.exe` / `Interim.exe` and `-sources`.

## Known interactions

[SmoothFrames](https://github.com/WhiteFang34/SmoothFrames) also patches the render thread. Jitter plus camera interpolation can interact once intercepts exist.

## Bug reports

Open an issue with **Show Status** text, GPU, driver version, and `SpaceEngineers.log`.
