# Pass-begin binds

Compile intercept is not enough. Extra SRVs and CBs must be bound when Keen draws lighting or post. Anomaly owns those Harmony prefixes and the unbind.

| Where | Slot | What |
|-------|------|------|
| Geometry GBuffer | b6 | Anomaly velocity CB — **VS only**. Every render context owns a ring of exact-layout 240-byte buffers; each entry is mapped at most once per frame. Pass begin writes a default, Stage 2 binds one entry per group, and old-pipeline cube/deformed draws bind one after `BindShaderBundle` with last main-view object-CB rows (`HasPrevWorld`). |
| Geometry GBuffer | b6 + PS b7 probe | `MrtWrite` clears to -X/cyan, emits runtime +X/pink from VS b6 and a dedicated immutable 16-byte PS b7 payload, and uses an RG-replace Target3 blend without changing Keen's Target0–2 behavior. Gray means an explicit zero. Status independently reflects and GPU-reads back both cbuffer layouts. |
| Geometry GBuffer | scheduler-end probe | `PassEndClear` records +X once on Keen's immediate context after `MyRenderScheduler.Done` executes all deferred geometry lists. Every depth-foreground pixel must be pink; `pass-end-clear=1` proves the final sampled velocity resource. |
| Geometry GBuffer | t15 / t16 | Previous worlds / previous bones — **VS only**. CPU packing runs during prepare; t15 upload is recorded on the Stage 2 GBuffer deferred context at pass begin. Each group rebinds VS b6 `InstanceBase` so `t15[SV_InstanceID + InstanceBase]` matches Keen's VB fetch. |
| Lighting / post | t5 | Catalog velocity (`AnomalyVelocityBuffer`) |
| Lighting | t6–t9 | Extra GBuffer color attachments, then `RequestSrv` leftovers |
| Atmosphere | t5 | Keen `DensityLut` — never steal this slot |
| Atmosphere | t6 | Catalog velocity |
| Atmosphere | t7–t9 | `RequestSrv` leftovers |
| Lighting / post / atmosphere | b6 | `AnomalyLightingExtras` CB — a different object from geometry b6. Includes jitter + unjittered VP. |

```csharp
// ClientPlugin.Shaders.ShaderBindRegistry
RequestSrv("Lighting", "linearDepth");          // next free t6–t9
RequestSrv("Post.Tonemap", "historyColor", 6);  // explicit slot
RequestSrv("Atmosphere", "linearDepth");        // t7+ (t5 DensityLut, t6 velocity)
```

> **Caution — Unbind.** Anomaly clears extra RT/SRV after each pass. If you write your own Harmony anyway, you will leak state into Rich HUD. Don’t.

→ [[HLSL-cookbook|Sample velocity in lighting HLSL]]
