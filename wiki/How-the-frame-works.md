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

## One frame, in order

Velocity and linear depth freeze at `MyRenderScheduler.Done` — before atmosphere, clouds, and OIT. SE-DLSS evaluates LDR after tonemap and owns Halton jitter. Atmosphere inject does not invent motion vectors.

→ [[Frame-graph|Full frame graph]]

> **Note — Why history is copied last.** TAA during post must still see frame N−1. If Anomaly copied LBuffer at scheduler Done, this frame’s post would already see the current picture as “history.”

## Why not replace the whole renderer

Minecraft shading is small, so Iris can swap the world renderer for a named set of programs. Space Engineers already has deferred GBuffer, tiled lights, CSM, HBAO, bloom, OIT, atmosphere, and GPU particles (about 215 HLSL files). Cloning that is not the product. Steal named stages, compile-time rewrite, and fallback. Do not steal “one pack owns the frame.” Pulsar loads many plugins at once.

→ [[Composition-rules|Composition when many plugins load]]
