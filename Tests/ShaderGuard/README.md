Run from the repository root:

```powershell
dotnet run --project Tests/ShaderGuard/ShaderGuard.csproj -c Release -- "T:/SteamLibrary/steamapps/common/SpaceEngineers/Content/Shaders"
```

Supply the installed game shader directory. The test copies six originals into a unique temporary fixture, checks the production hash guard, mutates one wrapper original and removes the other, then removes only its fixture. It never modifies installed shaders. Also links the production volume registry to check lazy status/invalidation and the terminal nesting patch to check owned recursion and isolation from another client using Harmony and a minimal Master fixture. No GPU or game session is started.

Debug volume sample logging is opt-in: set `ANOMALY_VOLUME_SAMPLES=1` before launching a Debug build. Release builds omit these samples.
