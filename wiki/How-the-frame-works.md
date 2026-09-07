# How the frame works

Keen already compiles hundreds of HLSL permutations. Anomaly hooks that compile once, then either injects includes, swaps a source file, or draws an extra fullscreen pass it owns.

```
0 Intercept → 1 Inject → 2 Overlay → 3 Owned + catalog
```

| Layer | You write | Who draws |
|-------|-----------|-----------|
| 0 Compile intercept | Nothing. Anomaly adds include dirs, defines, overlay resolve, cache identity. | Keen |
| 1 Additive inject | Snippets under `Inject/<Stage>.hlsli` | Keen, with extra MRT/SRV Anomaly binds |
| 2 Named overlay | A replacement file under `Overlay/<Stage>/` | Keen, using Anomaly-compiled bytecode |
| 3 Owned pass | `Fullscreen/<Slot>/*.hlsl` or a C# `Register` | Anomaly fullscreen (data-driven first), then you bind |

After Anomaly activates its include paths, defines, and pack overlays, it queues
frame-boundary calls to Keen's native `MyShaders.Recompile()` and
`MyMaterialShaders.Recompile()`. Together they rebuild Stage 2 shader IDs and
old-pipeline native material bundles that became resident before Pulsar loaded
Anomaly. They do not clear Keen's
shader cache, create another compiler, or run another renderer.

Anomaly also supplies the two small `Geometry/Passes/*Stage.hlsli` dispatchers.
Their GBuffer branch resolves through Anomaly's include root; every other branch
resolves to Keen's corresponding stage. This avoids depending on redirection of a
nested local include while still leaving every draw and pass implementation to Keen.

## One frame, in order

Velocity freezes at `MyRenderScheduler.Done` — before atmosphere, clouds, and OIT. Linear depth / Hi-Z / history color are produced only when a pack is live or the matching Debug buffer is on. The unique upscaler owns Halton jitter. When an AfterUpscale Display tenant is registered it should evaluate pre-tonemap HDR and publish the dest. Atmosphere inject does not invent motion vectors.

→ [[Frame-graph|Full frame graph]]

> **Note — Why history is copied last.** TAA during post must still see frame N−1. If Anomaly copied LBuffer at scheduler Done, this frame’s post would already see the current picture as “history.”

## Why not replace the whole renderer

Minecraft shading is small, so Iris can swap the world renderer for a named set of programs. Space Engineers already has deferred GBuffer, tiled lights, CSM, HBAO, bloom, OIT, atmosphere, and GPU particles (about 215 HLSL files). Cloning that is not the product. Steal named stages, compile-time rewrite, and fallback. Do not steal “one pack owns the frame.” Pulsar loads many plugins at once.

→ [[Composition-rules|Composition when many plugins load]]
