# Pass-begin binds

Compile intercept is not enough. Extra SRVs and CBs must be bound when Keen draws lighting or post. Anomaly owns those Harmony prefixes and the unbind.

| Where | Slot | What |
|-------|------|------|
| Geometry GBuffer | b6 | Anomaly velocity CB — **VS only**. Pass begin writes a default; Stage 2 rebinds immediately before each group draw; old-pipeline cube/deformed draws bind after `BindShaderBundle`. Each render context owns a ring of exact-layout 224-byte CBs, and every entry is mapped at most once per frame; this avoids Keen's size-keyed object-CB cache and deferred `MapDiscard` alias reuse. |
| Geometry GBuffer | t15 / t16 | Previous worlds / previous bones — **VS only**. t15 is instance-buffer order; Stage 2 b6 sets `InstanceBase` to the group's `OffsetInInstanceBuffer`, so `t15[SV_InstanceID + InstanceBase]` selects the group's base matrix copy. |
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
