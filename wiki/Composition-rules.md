# Composition rules

Iris packs are exclusive. Pulsar loads many plugins. These rules are the law so SE-DLSS, a TAA plugin, and a tonemap pack can coexist.

| # | Rule |
|---|------|
| 1 | Anomaly owns extra GBuffer attachments. Request a slot. |
| 2 | Defines are merged by Anomaly, not by each pack’s Harmony prefix. |
| 3 | Replace is exclusive per key. Inject is additive behind Anomaly includes. Fullscreen IsolatedAdd stacks; fullscreen Replace is exclusive per slot. |
| 4 | Consumers bind registry textures, ship `Fullscreen/` programs, or register `OwnedPassRegistry` draws. They do not patch `DrawGameScene`, atmosphere, or tonemap, and they do not call `Draw`. |
| 5 | `ClearState`, DRS, and device reset stay Anomaly’s problem. Extra RTs follow `ResolutionI`. Packs use `RegisterLifetime`. |
| 6 | `exclusive` GBuffer opts out of Anomaly-owned write stages. Atmosphere wrap needs exclusive Atmosphere. |
| 7 | Depth stays 3-attachment-free. Compile failure rolls back that pack. |
| 8 | Atmosphere inject does not fix DLSS. After `Scheduler.Done` use `ContributeVelocity` / `Reactive`. |

## Do not

| Idea | Why not |
|------|---------|
| Second Harmony compile hook | Two intercepts fight; Anomaly owns `MyShaderCompiler`. |
| Fork `Materials/Standard/Pixel.hlsl` for extras | 215-file tax. Inject shared stages. |
| Inject into `VertexTemplateBase` / `PixelTemplateBase` | Shared with Depth. |
| Widen Keen’s 64-byte instance VB | Shared with depth packing. |
| Hook `MyInstance.UpdateWorldMatrix` | History is keyed by ActorID after `UpdateMatrices`. |
| Workshop folders as packs | No `DependencyIds`, no asset SHA-256. |
| Leave RT/SRV bound | Rich HUD breaks. |
| Bind Anomaly extras at atmosphere t5 | Keen `DensityLut` lives there. Velocity is t6. |
| Patch the projection for jitter | SE-DLSS owns Halton. Read `FrameTemporal`. |
| Pack `MyPixelShaders.Create` / pack fullscreen RTs | Anomaly owns `FullscreenPassRegistry` and the scratch pair. |
| Last-writer-wins Replace on a slot | Same as silent Overlay overwrite. |

> **Warning — Exclusive overlay vs velocity.** Velocity inject still applies to a replaced Standard pixel only if that pixel still includes `Passes/PixelStage.hlsli`. A full-file replace that omits the include opts out of extras.

→ [[Troubleshooting|Rollback and Status]]
