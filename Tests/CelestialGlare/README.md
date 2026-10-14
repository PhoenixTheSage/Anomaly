# Celestial solar-glare ownership checks

This console harness executes the built `CelestialBackgroundRegistry` and actual
solar identity, unload and flare-prefix code against installed Space Engineers
types. It installs the exact Harmony prefix on Keen's three-argument flare draw
overload and invokes the prefix with the actual actor/light types. No copied
ownership policy is used.

Twenty-four assertions cover initial activation, successful current/previous
draws, worker-before/after-main ordering, stale/future/wrapped frame counters,
disable, failure, provider conflict, native opt-in, exact solar identity, unrelated
lights, sector unload, resource release, an unusable main draw and unregister.

From the Anomaly repository, build without deployment and run both targets:

```powershell
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never -p:UseSharedCompilation=false -m:1
dotnet build Tests/CelestialGlare/CelestialGlare.csproj -c Release
$gameBin = 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64'
dotnet Tests/CelestialGlare/bin/Release/net10.0/CelestialGlare.dll $gameBin "$PWD/ClientPlugin/bin/Release/net10.0/Anomaly.dll"
& ./Tests/CelestialGlare/bin/Release/net48/CelestialGlare.exe $gameBin "$PWD/ClientPlugin/bin/Release/net48/Anomaly.dll"
```

Replace the game path for another installation. Both targets passed on 2026-10-02.
The game renderer is not initialized. Only the successful-main-draw certificate
and `FrameTemporal` clock are seeded; GPU draws, real worker scheduling,
post-processing bloom and upscalers are not exercised. FinalFrontier's separate
D3D11 WARP suite verifies its actual optical shader. In-game appearance and the
bounded activation/failure transition still require game testing.
