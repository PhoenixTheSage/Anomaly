# DX11 pack compatibility regression

Build Anomaly and Prism.SSGI in Debug first, including their companions. Run:

```powershell
dotnet run --project Tests/Dx11PackCompatibility/Dx11PackCompatibility.csproj -f net10.0 -- net10.0 uploads
dotnet run --project Tests/Dx11PackCompatibility/Dx11PackCompatibility.csproj -f net10.0 -- net10.0 lookup
dotnet build Tests/Dx11PackCompatibility/Dx11PackCompatibility.csproj -f net48
./Tests/Dx11PackCompatibility/bin/Debug/net48/Dx11PackCompatibility.exe net48 uploads
./Tests/Dx11PackCompatibility/bin/Debug/net48/Dx11PackCompatibility.exe net48 lookup
```

The fixture loads actual core/companion assemblies. It suppresses only Keen's
`MyRender11` static game/platform bootstrap in an in-memory copy, sets a real
D3D11 hardware device, checks the installed Keen `ISrvBindable` contract, and
reads uploaded RGBA pixels through shader t7. It verifies upload retirement,
deferred lifetime callbacks, and suppression of the duplicate guest SVGF chain
on DX11. Lookup uses a temporary Pulsar source-cache layout and a renamed copy
of the current SSGI core, checks companion rebinding, GUID compatibility, and
rejects other pack prefixes. No game install, source configuration, or player
settings are changed. Full in-world effect visibility remains a separate check.
