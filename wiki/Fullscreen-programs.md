# Fullscreen programs

Anomaly owns the draw for pack pixel shaders under `Fullscreen/`. Overlay/Inject compose Keen **source**. Fullscreen composes **draws**. Packs do not call `Draw`, create RTs, or Harmony-patch Keen pass methods.

Resolve `ClientPlugin.Shaders.FullscreenPassRegistry`. Data-driven programs run first at each `OwnedPassSlot`; C# `OwnedPassRegistry.Register` is the escape hatch and runs after.

## Pack layout

```
Fullscreen/
  AfterAtmosphere/Curtain.hlsl
```

Folder defaults: compose `IsolatedAdd`, id `{packId}.{name}`, output `pass.{id}`, temporal `InColor`, priority from the pack. Skip `.hlsli`. Unknown slot fail closed. Shared helpers belong in `Inject/` so the compile include path sees them.

`anomaly.json` `passes[]` overrides those defaults:

```json
{
  "passes": [
    {
      "id": "example.curtain",
      "slot": "AfterAtmosphere",
      "file": "Fullscreen/AfterAtmosphere/Curtain.hlsl",
      "compose": "IsolatedAdd",
      "priority": 0,
      "temporal": ["InColor", "Reactive"],
      "output": "pass.example.curtain"
    }
  ]
}
```

## Compose

| Mode | Who | Dest |
|------|-----|------|
| IsolatedAdd (default) | Many, additive | Scratch then `src + dest` |
| IsolatedMix | Many, over | Scratch then `src + dest * (1 - src.a)` |
| Chain | Many, ordered | Each samples the previous isolated; last copies to dest |
| PublishOnly | Producer | Scratch only; catalog `pass.<id>` |
| Replace | One owner | Fail closed if two claim the slot |
| DirectAdd | Opt-in | Isolated then additive merge |

HDR slots (AfterLighting / AfterAtmosphere / AfterTransparent / BeforeTonemap) merge into `LBuffer`. AfterTonemap merges into Keen’s tonemap result. AfterUpscale publishes isolated; merge is skipped unless a consumer passed a dest.

## Bus (fixed)

`#include <AnomalyFullscreen.hlsli>`

| Slot | What |
|------|------|
| t0 | Scene color (`LBuffer`, LDR dest, or previous isolated in Chain) |
| t1 | `linearDepth` |
| t2 | `velocity` |
| t3 | `reactiveMask` |
| b6 | Extras: size, jitter, unjittered VP, prev VP, frame |
| b7 | `AnomalyPassUniform0–3` from `FullscreenPassRegistry.SetUniforms(id, float[16])` |

Do not steal atmosphere t5. AfterAtmosphere runs after Keen unbinds `DensityLut`.

## Temporal

Folder default is `InColor`. After `Scheduler.Done` that is color-in / motion-out unless you also set `Reactive` and/or `ContributeVelocity`. Anomaly logs once per program id (`motion-out`). Atmosphere inject still does not invent motion vectors.

C# uniforms:

```csharp
var t = assembly.GetType("ClientPlugin.Shaders.FullscreenPassRegistry");
t?.GetMethod("SetUniforms")?.Invoke(null, new object[] { "example.curtain", new float[] { intensity, 0, 0, 0 } });
```

## Catalog

| Name | Meaning |
|------|---------|
| `fullscreenIsolated` | Last isolated output this frame (reserved) |
| `pass.<id>` | That program’s isolated RT |

Debug buffer **FullscreenIsolated** overlays the last isolated output. Show Status lists `Fullscreen: slot/compose:id`.

→ [[Owned-passes|C# slot register]] · [[Overlay-vs-inject|Source compose]] · [[HLSL-cookbook|PS snippet]]
