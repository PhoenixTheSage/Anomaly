# Framework regression checks

Build `dotnet build Tests/Framework/Framework.csproj -c Release`, then run either
`Tests/Framework/bin/Release/net48/Framework.exe Assets/Shaders/AnomalyVolumeCommon.hlsli`
or the net10.0 executable from the repository root.

The suite links the production art queue, interior uploader, medium registry,
texture checkpoint and composite recovery helper. Game snapshot/manager/context plumbing
is substituted; GPU buffer creation, mapping, copies and readback run on real D3D11 WARP.
It checks art cancellation/retirement, rejected replacements, failed uploads,
topology/bounds/transforms/reordering/stale snapshots, outstanding deferred buffer writes,
partial-composite rollback and replay, success, copy failure, device-loss routing and
resetting frozen providers even when a callback throws.

These checks do not establish game integration, hardware GPU cost, or visual acceptance.
