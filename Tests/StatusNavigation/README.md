# Status navigation regression

Build all runtime plugin projects first in Debug. This fixture exercises their actual assemblies with a recording terminal registry, including delayed Status registration after Settings succeeds, failed population cleanup, idempotence, callback ownership, renderer headers and device isolation.

Run for `net48` and `net10.0` on Windows (replace paths as appropriate):

```powershell
dotnet run --project Tests/StatusNavigation/StatusNavigation.csproj -f net10.0 -- net10.0 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64'
dotnet run --project Tests/StatusNavigation/StatusNavigation.csproj -f net10.0 -- host 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64' net10.0 sources vulkan
dotnet run --project Tests/StatusNavigation/StatusNavigation.csproj -f net10.0 -- host 'T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64' net10.0 sources dx11
```

Repeat host cases with `local` instead of `sources`. Host cases load the real Anomaly core and, for DX11, the matching companion. They verify one shared registry, diagnostics before route/device initialization, both settings routes, correct persistence ownership, and source compilation's generated core identity. They create no GPU device or game session. In-world Rich HUD Master display/input is a separate manual check.
