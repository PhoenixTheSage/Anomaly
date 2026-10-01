# Frame graph

Keen’s order, not a pack’s. Velocity is published at `MyRenderScheduler.Done` — before atmosphere and transparent. Linear depth / Hi-Z / history color run only when a pack is live or Debug buffer asks for them. Idle Anomaly (no packs) must not pay those full-res copies.

| Moment | Who | Live state |
|--------|-----|------------|
| GBuffer + velocity MRT | Keen + inject | Object MVs on geometry pixels |
| Lighting | Keen + Light wrap | Velocity at t5, extras CB b6. `PreparePointLights` publishes catalog `pointLights` / `tileIndices` (wrap, no copy). AfterLighting BeforeFullscreen can `RequestOccupancy` / `RequestPointShadows`. |
| Scheduler.Done | Anomaly | Publish `velocity`. `linearDepth` / `hiZ` only if a pack or debug wants them |
| AfterLighting | `OwnedPassRegistry` + `FullscreenPassRegistry` | HDR LBuffer; atmosphere not yet. HDR fullscreen binds `FrameConstants` at b0 and point lights at t10/t11. `IsolatedSub` blends `dest*(1-src)` onto dest (`AnomalyIsolatedSub`). Recorded on Keen’s transparent deferred worker (`DoWork` / `AcquireRC`). BeforeFullscreen C#, then `litMips` if wanted, then `Fullscreen/`, then AfterFullscreen C#. HDR slots skip LCD / TargetView / TargetCamera. |
| Atmosphere | Keen + Common wrap | DensityLut t5; Anomaly velocity t6, extras t7+, CB b6 |
| AfterAtmosphere | Same registries | After unbind, same deferred `rc` as AfterLighting. Aurora-class: `Fullscreen/` or C#. Use `ctx.Rc`. Anomaly redirects `MyRender11.RC` during the callback. |
| Clouds / OIT / top billboards | Keen | Transparent emission. No new MVs unless contributed |
| AfterTransparent | `OwnedPassRegistry` | OIT done |
| BeforeTonemap | `OwnedPassRegistry` | HDR, internal / DRS res |
| Tonemap | Keen (or skipped) | HDR → LDR. Skip when `HasDisplayTenant && !HasUpscaleConsumer`. Unique upscalers may skip Keen after notify. Every skip still needs a dest so `DrawGameScene` can highlight / FXAA / copy; Anomaly adopts the notified dest when `__result` is null. If Keen’s `DrawGameScene.Tonemapped` (and FXAA / CA) is 8-bit UNORM, Anomaly wraps `R16G16B16A16_Float` so BT.2390 values above 1 are not clipped on store. |
| AfterTonemap | `OwnedPassRegistry` (First) | Internal LDR if Keen ran, before upscale evaluate |
| Upscale evaluate | Unique consumer (`ClaimUpscale`) | `hdrColor` when a Display tenant exists; else LDR. Jitter owner |
| AfterUpscale | `NotifyUpscaleComplete(rc, color)` | Output res after an upscaler. Catalog `upscaledColor` + t0 + `ctx.SceneColor`. Display-without-upscale grades `LBuffer` into the borrowed dest at `ResolutionI` before `DrawGameScene` continues. Fallback at `DrawGameScene` postfix only if nobody notified. |
| History + debug | Anomaly | `historyColor` copy only if a pack or debug wants it; catalog debug is `Priority.Last` at `ViewportResolution` (covers DLSS output) |

## Jitter

SE-DLSS owns Halton jitter (`Projection.M31` / `M32`). `FrameTemporal` reads it and republishes `AnomalyUnjitteredViewProj` / `AnomalyPrevViewProj` / `AnomalyLightingJitter` / `AnomalySafetyScale` / `AnomalySunColor` / `AnomalySkyAmbient` / `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil` / `AnomalyVolumeSkipFloor` / `AnomalyVolumeSkipMul` on the extras CB (lighting, atmosphere, post — append-only, **320 B**). Do not patch the projection.

> **Note — Color bus.** AfterUpscale is a scheduler. The unique upscaler calls `ClaimUpscale` then `NotifyUpscaleComplete(rc, dest)` so catalog `upscaledColor` is the dest at output resolution. Display tenants (`TemporalPolicy.Display`) read `ctx.SceneColor`, not raw `LBuffer`. Display without an upscaler grades `LBuffer` into the dest at `Run` so `DrawGameScene` can copy. That dest is Keen’s borrow when it is already fp16; otherwise Anomaly wraps an fp16 UAV. Display with an upscaler: if `Run` returns null, Anomaly adopts the notified dest. If nobody notifies, Anomaly runs the slot once at `DrawGameScene` postfix (native res, `LBuffer`). Anomaly does not present.

→ [[Owned-passes|Register a slot]] · [[Pass-begin-binds|Atmosphere t6]]
