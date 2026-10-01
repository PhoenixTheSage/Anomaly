# Glossary

| Term | Meaning here |
|------|----------------|
| Keen | Stock Space Engineers renderer / HLSL under `Content/Shaders`. |
| Permutation | One compile of a Keen shader with a macro set (`RENDERING_PASS`, `DEPTH_ONLY`, …). |
| Named stage | Public pack folder name mapped to a small set of Keen files. |
| Inject | Additive HLSL concatenated into `Anomaly/Extras/<Stage>.hlsli`. |
| Overlay | Replacement source for one compile key. One owner. |
| Exclusive | `anomaly.json` claim that opts a pack into overlaying Anomaly-owned wraps. |
| Sentinel | Probe compile after packs apply; failure rolls that pack back. |
| Catalog | Named `ISharedBuffer` lookup. No compile-time Anomaly reference. |
| Owned pass | Anomaly HLSL + Anomaly draw, then publish — or a pack draw at an `OwnedPassSlot`. |
| Fullscreen program | Pack PS under `Fullscreen/<Slot>/`. Anomaly compiles and draws (`FullscreenPassRegistry`). |
| FullscreenCompose | IsolatedAdd, IsolatedMix, IsolatedSub, Chain, PublishOnly, Replace, DirectAdd. |
| OwnedPassSlot | AfterLighting, AfterAtmosphere, AfterTransparent, BeforeTonemap, AfterTonemap, AfterUpscale. |
| TemporalPolicy | `InColor` \| `ContributeVelocity` \| `Reactive` \| `Display`. |
| Color bus | Catalog `hdrColor` (LBuffer) + `upscaledColor` (notify dest). AfterUpscale is the clock; Display tenants sample `ctx.SceneColor`. |
| FrameTemporal | SE-DLSS jitter read + unjittered VP republish. Do not patch Projection. |
| `Keen/` include | Compile intercept opens `Content/Shaders` and skips overlay remap. |
| Keen patch | Load-time delta on a hashed `Content/Shaders` file. Mismatch disables GBuffer injection. |
| DRS | Dynamic resolution. Extra RTs follow `MyRender11.ResolutionI`. |
| Complementary depth | Hardware depth; `compute_depth` turns it into positive view Z. |
| Fail closed | On conflict, keep Keen/Anomaly default; do not last-writer-wins. |
| Framework gap | Pack workaround the next shader will also need. File on Anomaly (`Docs/Extensibility.md` Slice AI, [[Framework-gaps]]) in the same turn. |
| Slice AI | Extras-CB sun (`AnomalySunColor`) / IsolatedMix `LBuffer` units / `AnomalyMarchSteps` / `AnomalyVolumeViewLod` / `AnomalySunVisibility`. Shipped. |
| Slice AJ | Extras-CB night fill (`AnomalySkyAmbient` = `SunColor * 0.028` / `AnomalyVolumeAmbient`). Shipped. Do not hdr-lift `AnomalySkyLuma`. Do not scale by `AmbientForwardPass`. |
| Slice AS | AfterAtmosphere empty skip. Extras `AnomalyVolumeSkipFloor` / `AnomalyVolumeSkipMul` (320 B tail). Helpers `AnomalyVolumeSkipDt` / `AnomalyVolumeAdvance`. Fail closed. Do not bake an SDF Texture3D. Occupancy source remains pack weather. |
| Slice AT | Planet-shell weather UV. Helper shipped (`AnomalyPlanetShellUv` / `AnomalyPlanetSamplePos`: east/north metres, plus `ChartWeight` / `ChartDir` for a longitude chart). Shape tiles use `AnomalyPlanetShapeArcMetres` (azimuthal from the nearer pole). Do not floor `cos(lat)`. Packs must not invent a global `centerH` sphere or `dot(dir, axis)` weather UV. Clouds: 64×64×2 coverage; the climate map wraps in longitude. |
| Anomaly Shaders | Rich HUD terminal root. Framework pages live under **Anomaly**; packs add sibling titles. Master optional. |
| Hud overlay | Corner status via `HudOverlayRegistry`. Master optional; getter is a cached string, not a config write. |
