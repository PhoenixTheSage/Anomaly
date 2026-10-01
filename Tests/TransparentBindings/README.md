# AfterAtmosphere transparent bindings regression

Links production `TransparentStageBindings` unchanged. Engine adapters wrap real
D3D11 WARP stages and model Keen's reference-cache behavior. A pixel consumer
reads frame b0 and a wrapped atlas sampler, representing the bindings billboard
shaders inherit. This is not a full game/actual thruster shader test.

The probe reproduces zero emission after the isolated contributor's cleared b0,
then checks exact recovery with restoration. It runs on immediate and deferred
contexts, covers failure cleanup and repeated cache-skipped binds, and checks
standard/shadow sampler cache values. No render resources or plugins are installed.

```powershell
dotnet run --project Tests/TransparentBindings/TransparentBindings.csproj -c Release -f net10.0
dotnet build Tests/TransparentBindings/TransparentBindings.csproj -c Release -f net48
./Tests/TransparentBindings/bin/Release/net48/TransparentBindings.exe
```

Expected: **32 assertions per runtime**. Requires SharpDX DLLs at the installed
game Bin64 path in the project. Native shader compilation uses D3DCompiler.
