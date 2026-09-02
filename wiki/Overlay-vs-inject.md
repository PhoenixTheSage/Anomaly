# Overlay vs inject

Default to inject. Overlay is exclusive per file. Two packs claiming the same Keen path fail closed — Keen/Anomaly default is kept, both ids are logged.

| | Inject | Overlay | Fullscreen |
|--|--------|---------|------------|
| Intent | Add helpers / sample extras | Replace a program | Anomaly-drawn composite |
| Folder | `Inject/<Stage>.hlsli` | `Overlay/<Stage>/<file>` | `Fullscreen/<Slot>/<file>.hlsl` |
| Lands in | `Anomaly/Extras/<Stage>.hlsli` | That Keen (or Anomaly) compile key | `FullscreenPassRegistry` |
| Conflict | Concatenated (additive) | One owner; fail closed | IsolatedAdd stacks; Replace fail closed |
| Who draws | Keen | Keen | Anomaly `Draw(3)` |

## Inject mapping

`Inject/GBuffer.hlsli` or `Inject/GBuffer/*.hlsli` concatenates into GBuffer extras (also included from pixel stage). `Inject/Lighting.hlsli` is visible to LightDir / Point / Spot via the Light wrap. `Inject/Atmosphere.hlsli` is included from the AtmosphereCommon wrap. Unscoped `Inject/*.hlsli` still goes to GBuffer (v1). Unknown folders fail closed.

> **Note — Pixel-only helpers.** Wrap pixel code in `#ifdef ANOMALY_PIXEL_STAGE`. GBuffer PS defines it; VS does not. Lighting extras see `ANOMALY_LIGHTING_STAGE`.

## Overlay mapping

Put a unique basename or path suffix under the stage folder. `Overlay/GBuffer/PixelStage.hlsli` becomes `Geometry/Passes/GBuffer/PixelStage.hlsli`. Unknown files under a stage name are skipped (fail closed). Escape hatch: Keen-relative `Overlay/Geometry/Materials/Standard/Pixel.hlsl`.

## Exclusive flags

| What you overlay | Required exclusive |
|------------------|--------------------|
| GBuffer write stages (`VertexStage`, `PixelStage`, `GBufferWrite.hlsli`) | `["GBuffer"]` — opts out of Anomaly velocity extras on those files |
| GBuffer read wraps (`GBuffer.hlsli`, `Surface.hlsli`) | `["GBuffer"]` or `["Lighting"]` |
| `Lighting/Light.hlsli` | `["Lighting"]` — not Lighting.Dir / .Point / .Spot |
| `Transparent/Atmosphere/AtmosphereCommon.hlsli` | `["Atmosphere"]` — wrap includes Keen via `Keen/` prefix |

`Lighting` is inject-only as a folder name. To replace the wrap, use the Keen-relative overlay path. The Atmosphere wrap is Anomaly-owned; `Inject/Atmosphere.hlsli` is the additive path. Do not steal t5 — that is Keen `DensityLut`.

Fullscreen is the runtime analog: IsolatedAdd is inject, Replace is exclusive overlay. See [[Fullscreen-programs|Fullscreen programs]].

→ [[HLSL-cookbook|Copy-paste HLSL]]
