# Frame graph

Keen’s order, not a pack’s. The important freeze: **velocity** is published at `MyRenderScheduler.Done` — before atmosphere and transparent. `linearDepth` / `hiZ` / `historyColor` are produced only when a pack is live or the matching Debug buffer is on. Idle Anomaly (no packs) must not pay those full-res copies.

| Moment | Who | Live state |
|--------|-----|------------|
| GBuffer + velocity MRT | Keen + inject | Object MVs on geometry pixels |
| Lighting | Keen + Light wrap | Velocity at t5, extras CB b6 |
| Scheduler.Done | Anomaly | Publish `velocity`. `linearDepth` / `hiZ` only if a pack or debug wants them |
| AfterLighting | `OwnedPassRegistry` + `FullscreenPassRegistry` | HDR LBuffer; atmosphere not yet. `Fullscreen/` first, then C# |
| Atmosphere | Keen + Common wrap | DensityLut t5; Anomaly velocity t6, extras t7+, CB b6 |
| AfterAtmosphere | Same registries | After unbind. Aurora-class: `Fullscreen/` or C# |
| Clouds / OIT / top billboards | Keen | Transparent emission. No new MVs unless contributed |
| AfterTransparent | `OwnedPassRegistry` | OIT done |
| BeforeTonemap | `OwnedPassRegistry` | HDR, internal / DRS res |
| Tonemap | Keen | HDR → LDR |
| AfterTonemap | `OwnedPassRegistry` (First) | Internal LDR, before SE-DLSS evaluate |
| SE-DLSS evaluate | Consumer | LDR + `VelocityRegistry.Active`. Jitter owner. |
| AfterUpscale | `NotifyUpscaleComplete` | Output res; fallback at `DrawGameScene` if nobody notifies |
| History + debug | Anomaly | `historyColor` copy only if a pack or debug wants it; catalog debug is `Priority.Last` |

## Jitter

SE-DLSS owns Halton jitter (`Projection.M31` / `M32`). `FrameTemporal` reads it and republishes `AnomalyUnjitteredViewProj` / `AnomalyPrevViewProj` / `AnomalyLightingJitter` on the extras CB (lighting, atmosphere, post — append-only). Do not patch the projection.

> **Note — Upscale notify.** SE-DLSS should call `OwnedPassRegistry.NotifyUpscaleComplete()` after evaluate. AfterUpscale then runs at output resolution. If nobody notifies, Anomaly runs the slot once at `DrawGameScene` postfix (native res).

→ [[Owned-passes|Register a slot]] · [[Pass-begin-binds|Atmosphere t6]]
