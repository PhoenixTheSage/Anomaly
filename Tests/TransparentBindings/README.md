# Transparent consumer bindings regression

Links production `TransparentStageBindings` unchanged. Engine adapters wrap real
D3D11 WARP stages and model Keen's reference-cache behavior. A pixel consumer
reads frame b0 and a wrapped atlas sampler, representing the bindings billboard
shaders inherit. This is not a full game/actual thruster shader test.

The probe reproduces zero emission after the isolated contributor's cleared b0,
then checks exact recovery with restoration. It runs on immediate and deferred
contexts, covers failure cleanup and repeated cache-skipped binds, and checks
standard/shadow sampler cache values. No render resources or plugins are installed.

The October 2 Star Dust follow-up adds restoration at the shared
`MyBillboardRenderer.BindResourcesCommon` boundary: DLSS/FSR output-depth
preparation clears the context after the earlier atmosphere cleanup. The
expanded runner compiles installed Keen `Transparent/Billboards.hlsl` with
SOFT_PARTICLE. A synthetic atlas, billboard record and vertex shader exercise
the actual pixel shader. Missing PS b0 erases its soft-depth output; missing
LinearSampler uses default addressing and samples the transparent fixture edge.
Restoration recovers exact healthy pixels and preserves soft foreground clipping.
It also installs the actual built Anomaly postfix on the installed renderer.

After building Anomaly without deployment, run:

```powershell
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never -p:UseSharedCompilation=false -m:1
pwsh -File Tests/TransparentBindings/Run.ps1
```

Expected: **43 assertions on each runtime**, including original probes, actual
pixel shader and hook installation. Override `-GameShaders`, `-Fxc` and
`-AnomalyRoot` for another installation. Shader output is temporary and removed
after the run. These tests do not invoke the full game renderer, DLSS/FSR depth
implementation or actual scene; in-game appearance and hardware cost are pending.

```powershell
dotnet run --project Tests/TransparentBindings/TransparentBindings.csproj -c Release -f net10.0
dotnet build Tests/TransparentBindings/TransparentBindings.csproj -c Release -f net48
./Tests/TransparentBindings/bin/Release/net48/TransparentBindings.exe
```

The original probes alone (commands above without shader arguments) retain
**32 assertions per runtime**. Requires SharpDX DLLs at the installed
game Bin64 path in the project. Native shader compilation uses D3DCompiler.
