# Shader parity regression gates

The 2026-10-21 review reproductions are maintained in `ParityContracts.cs`.
Run `dotnet run --project Tests/GuestRuntime -c Release -- . --parity-contracts`
from the Anomaly root. This focused gate checks desired behavior, so it fails
on the baseline renderer: 95x73 at half resolution becomes 48x36 rather than
DX11's 47x36. Subsequent checks cover all compose histories and an early
consumer of a later producer's previous-frame alias.

The normal GuestRuntime suite compiles actual owned shader sources through
SE-Vulkan's compiler. `--gpu` adds real native pipeline and image execution;
it requires hardware and never silently skips missing Vulkan support.

Baseline source: Anomaly `0f1e855`, SE-Vulkan `b296145`.
Baseline native SHA256:
`4AB949A5E920131504B5FD36B474EB486D4703BBA38AD937B5B1587AD5524191`.
Baseline Anomaly net10 SHA256:
`B7ED5CAD7592467E34F308F62B4455324C81F56559E201F6BF6A4B494F452F17`.

After repairing source-linked dependencies, GuestContract and GuestRuntime
pass without external MSBuild imports. The actual-core GuestDepthGpuTests
baseline passes **274** checks, validation enabled, zero reported validation
errors. Its controlled interior transaction permits one retry only for the
documented stale first-use snapshot; production freshness limits are unchanged.

Existing stock timing evidence is recorded in SE-Vulkan's
`Docs/planning/NEXT-SESSION-BELOW-10MS.md`: paired Vulkan present cadence
10.45 / 10.72 ms and same-save DX11 about 4.4 ms. Those are historical control
measurements, not acceptance of this shader parity implementation. The final
matched Windows live matrix and new interleaved performance controls remain
required before full parity can be claimed.
