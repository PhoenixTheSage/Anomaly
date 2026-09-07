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
| Tonemap | Keen (or skipped) | HDR → LDR. Skip when `HasDisplayTenant` so the upscaler can evaluate HDR |
| AfterTonemap | `OwnedPassRegistry` (First) | Internal LDR if Keen ran, before upscale evaluate |
| Upscale evaluate | Unique consumer (`ClaimUpscale`) | `hdrColor` when a Display tenant exists; else LDR. Jitter owner |
| AfterUpscale | `NotifyUpscaleComplete(rc, color)` | Output res. Catalog `upscaledColor` + t0 + `ctx.SceneColor`. Fallback at `DrawGameScene` if nobody notifies |
| History + debug | Anomaly | `historyColor` copy only if a pack or debug wants it; catalog debug is `Priority.Last` at `ViewportResolution` (covers DLSS output) |

## Jitter

SE-DLSS owns Halton jitter (`Projection.M31` / `M32`). `FrameTemporal` reads it and republishes `AnomalyUnjitteredViewProj` / `AnomalyPrevViewProj` / `AnomalyLightingJitter` on the extras CB (lighting, atmosphere, post — append-only). Do not patch the projection.

> **Note — Color bus.** AfterUpscale is a scheduler. The unique upscaler calls `ClaimUpscale` then `NotifyUpscaleComplete(rc, dest)` so catalog `upscaledColor` is the dest at output resolution. Display tenants (`TemporalPolicy.Display`) read `ctx.SceneColor`, not raw `LBuffer`. If nobody notifies, Anomaly runs the slot once at `DrawGameScene` postfix (native res, `LBuffer`). Anomaly does not present.

→ [[Owned-passes|Register a slot]] · [[Pass-begin-binds|Atmosphere t6]]
