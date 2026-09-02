# Troubleshooting

In-game: Anomaly settings → Show Status. Also `SpaceEngineers.log` and Anomaly’s debug log. Compile errors include `pack=id`.

| Symptom | Likely cause | What to do |
|---------|--------------|------------|
| Compile intercept: not live | Hook failed or assets not loaded | Confirm `LoadAssets` ran; Shaders folder next to the DLL. |
| Crash finishing world load (`construct_matrix_43`) | GBuffer PS compiled Keen’s VS-only helper | Rebuild Anomaly so `Anomaly.hlsli` deploys. Velocity reconstruct is VS-only (`ANOMALY_PIXEL_STAGE`). |
| Crash finishing world load (`AnomalyInitAttachments` / X3508) | Empty `inout GbufferOutput` — FXC requires every `SV_Target` field written | Rebuild so `GBufferWrite` / generated `GBufferAttachmentInit.hlsli` zero the struct. Hits old pipeline, Decals, Foliage, Sprites. |
| Depth / shadows broken | Pack overlay added a target or failed a sentinel | Status `rolled-back=N`. Remove exclusive GBuffer overlay or fix HLSL. |
| Pack missing from Status | Register used `Plugin.Instance` or ran before Anomaly loaded | Register on static `ShaderPackRegistry` from `LoadAssets`. |
| Magenta debug overlay | `HistoryValid` false | Look around one frame; avoid huge camera cuts; wait after resize. |
| Debug overlay is a small rectangle with DLSS | Backbuffer `Size` follows internal `ResolutionI` after `SetDRS` | Rebuild this slice. Overlay viewport is `ViewportResolution` (output). |
| Geometry mid-gray, sky has MVs | Composite kept GBuffer clear-zero on depth pixels | Rebuild. Composite camera-fills pixels that never wrote Target3. |
| `historyColor` unavailable | First frame or copy failed | Expected until one `DrawGameScene` postfix copy. |
| Lighting inject compiles but samples black | SRV not bound / wrong stage name | Use `Inject/Lighting.hlsli`; `RequestSrv` if you need a non-velocity catalog texture. |
| Aurora-class ghosting with DLSS | Color after `Scheduler.Done`, no MVs | Owned pass + `ContributeVelocity` / `Reactive`. Overlay Atmosphere alone cannot fix it. |
| Atmosphere extras sample black | Bound at t5 or after `RenderEnd` | Use t6. Anomaly rebinds per planet. |
| Thread CPU Load ~5–11 ms above vanilla | `Thread CPU Load` is `Parallel.Scheduler` (all workers), not the GPU. A Harmony **Prefix** on `MyGBufferPass.RecordCommandsInternal` ran for every voxel proxy; forked `Begin` MapDiscarded the velocity CB under a lock. | Rebuild this slice (transpiler + once-per-frame CB). A/B **CameraOnly** if leftover is the 4th GBuffer MRT. |
| Overlay ignored | Unknown file under a stage name | Match a mapped basename or use Keen-relative escape hatch. |
| Two packs, neither applies | Same overlay key | Fail closed. Split files or pick one owner. |
| Fullscreen: none | No `Fullscreen/<Slot>/*.hlsl` or unknown slot | One level: `Fullscreen/AfterAtmosphere/Name.hlsl`. Check Status `Fullscreen:`. |
| Two Replace, nothing draws | Two Replace on one slot | Fail closed. One Replace owner, or IsolatedAdd. |
| Isolated curtain ghosts with DLSS | InColor after Done, no MVs | `temporal` Reactive / ContributeVelocity. Overlay Atmosphere cannot fix it. |
| Fullscreen PS cannot include pack helpers | File not on the include path | Put shared `.hlsli` in `Inject/`. |

## Status fields that matter

Compile intercept live + include path. Shader packs / rolled-back. GBuffer attachments. Pass binds. Owned passes. Fullscreen programs. Frame temporal (jitter / history). Camera velocity live. Owned buffers sizes. Velocity buffer WxH, convention, history valid. Catalog debug mode.

→ [[Your-first-pack|Back to pack setup]]
