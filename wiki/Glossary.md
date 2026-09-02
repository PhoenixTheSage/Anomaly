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
| FullscreenCompose | IsolatedAdd, IsolatedMix, Chain, PublishOnly, Replace, DirectAdd. |
| OwnedPassSlot | AfterLighting, AfterAtmosphere, AfterTransparent, BeforeTonemap, AfterTonemap, AfterUpscale. |
| TemporalPolicy | `InColor` \| `ContributeVelocity` \| `Reactive`. |
| FrameTemporal | SE-DLSS jitter read + unjittered VP republish. Do not patch Projection. |
| `Keen/` include | Compile intercept opens `Content/Shaders` and skips overlay remap. |
| DRS | Dynamic resolution. Extra RTs follow `MyRender11.ResolutionI`. |
| Complementary depth | Hardware depth; `compute_depth` turns it into positive view Z. |
| Fail closed | On conflict, keep Keen/Anomaly default; do not last-writer-wins. |
