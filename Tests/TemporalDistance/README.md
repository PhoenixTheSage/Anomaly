# Temporal distance contract tests

References Anomaly's compiled Release assembly and installed game dependencies.
Build Anomaly for net48 and net10.0 first, with deployment disabled. Uses private
registration paths with a temporary shader filename to test host state; it does
not draw or install the plugin.

From the Anomaly repository:

```powershell
dotnet run --project Tests/TemporalDistance/TemporalDistance.csproj -c Release -f net10.0
dotnet build Tests/TemporalDistance/TemporalDistance.csproj -c Release -f net48
./Tests/TemporalDistance/bin/Release/net48/TemporalDistance.exe
```

Expected: 15 assertions per runtime. Checks opt-in/default/live scale, rejection,
reload/device-release persistence and the contributor constant-buffer layout.
Hardware pixel/motion probes are in the external Aurora review harness.
