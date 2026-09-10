# Fullscreen programs

Anomaly owns the draw for pack pixel shaders under `Fullscreen/`. Overlay/Inject compose Keen **source**. Fullscreen composes **draws**. Packs do not call `Draw`, create RTs, or Harmony-patch Keen pass methods.

Resolve `ClientPlugin.Shaders.FullscreenPassRegistry`. Order at each slot: C# `OwnedPassPhase.BeforeFullscreen`, then data-driven programs, then AfterFullscreen C# (the default `Register`).

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

HDR slots (AfterLighting / AfterAtmosphere / AfterTransparent / BeforeTonemap) merge into `LBuffer`. AfterTonemap merges into Keen’s tonemap result. AfterUpscale t0 is catalog `upscaledColor` when the unique consumer notified with a dest; Isolated still publishes; merge writes into that dest when it is an RTV. Without a dest, t0 falls back to `LBuffer` (wrong at output res).

## Bus (fixed)

`#include <AnomalyFullscreen.hlsli>`

| Slot | What |
|------|------|
| t0 | Scene color (`LBuffer`, LDR dest, `upscaledColor` at AfterUpscale, or previous isolated in Chain) |
| t1 | `linearDepth` |
| t2 | `velocity` |
| t3 | `reactiveMask` |
| t4–t6 | HDR slots: Keen `GBuffer0` / `GBuffer1` / `GBuffer2`. AfterTonemap / AfterUpscale: `avgLuminance` / `bloom` / `dirt` |
| t7–t9 | Pack catalog extras (`RequestSrv` or json `passes[].binds`) |
| s0 / s1 / s2 | Point / Linear (clamp) / `CloudSampler` wrap (`AnomalyWrapSampler`) |
| b6 | Extras: size, jitter, unjittered VP, prev VP, frame, `AnomalyCameraToWorld` (3×4), `AnomalyProjScale` (proj M11/M22), `AnomalySafetyScale` (1 = calm) |
| b7 | `AnomalyPassUniform0–15` from `FullscreenPassRegistry.SetUniforms(id, float[64])`. `0–7` are the original 128 B; `8–15` append to 256 B (same size as extras). Longer arrays fail closed; Anomaly logs once per id. |

Do not steal atmosphere t5. AfterAtmosphere runs after Keen unbinds `DensityLut`. Display AfterUpscale programs sample t4–t6 tonemap inputs; HDR slots sample GBuffer there instead. Pack extras always land at t7–t9.

C# extra SRVs:

```csharp
t?.GetMethod("RequestSrv")?.Invoke(null, new object[] { "example.curtain", "litMips", 7 });
t?.GetMethod("SetEnabled")?.Invoke(null, new object[] { "example.curtain", enabled });
```

`SetEnabled(id, false)` is the pack checkbox. It does not override Replace fail-closed. Status shows a trailing `-` when the pack disabled the program. Draw runs only when both the conflict flag and the pack flag are true.

Catalog `litMips` is generated at AfterLighting when a live program bound that name (json `binds` or `RequestSrv`), `BufferCatalog.RequestLitMips(mipLevels)` was called, or Debug view is LitMips. Default 5 mips of this-frame `LBuffer` (before atmosphere). Packs do not `GenerateMips` themselves.

## Temporal

Folder default is `InColor`. After `Scheduler.Done` that is color-in / motion-out unless you also set `Reactive` and/or `ContributeVelocity`. IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly with `Reactive` stamp dilated isolated luma into `reactiveMask` (2px, max-channel × 8, max with prior stamps) on the slot’s `rc` — AfterAtmosphere is Keen’s transparent deferred worker. Anomaly clears/stamps on that `rc` and redirects `MyRender11.RC` during AfterLighting / AfterAtmosphere / AfterTransparent callbacks. Anomaly logs once per program id (`motion-out`). Atmosphere inject still does not invent motion vectors. C# `ContributeVelocity` is still required if you want reconstructed curtain flow instead of a history reject.

## Camera safety

`FrameTemporal.SafetyScale` is this-frame camera move / look (1 = calm, 0 ≈ 80 m cut). It is written to extras as `AnomalySafetyScale` every fullscreen and lighting extras upload. Calm frames write `1` — same ALU as the old pad.

March packs should scale their step count:

```hlsl
int steps = (int)(stepBudget * saturate(AnomalySafetyScale) + 0.5);
```

Load-time lint (warn only) flags `while (` and `for` bounds that are not an integer literal or `#define` integer. Denoisers compiled outside `Fullscreen/` are not scanned.

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

Debug buffer **FullscreenIsolated** overlays the last isolated output. Show Status lists `Fullscreen: slot/compose:id` (a trailing `-` means `SetEnabled(false)`). HDR slots skip LCD / TargetView / TargetCamera.

→ [[Owned-passes|C# slot register]] · [[Overlay-vs-inject|Source compose]] · [[HLSL-cookbook|PS snippet]]
