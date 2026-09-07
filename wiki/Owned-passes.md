# Owned passes

Two layers: Anomaly-drawn fullscreen products, and a scheduler packs register into. Resolve `ClientPlugin.Shaders.OwnedPassRegistry`. Anomaly owns the Harmony. Packs do not patch atmosphere or tonemap. Prefer [[Fullscreen-programs|Fullscreen programs]] (`Fullscreen/<Slot>/*.hlsl`) so the pack does not own a draw. Data-driven programs run first; C# `Register` is the escape hatch.

## Anomaly products

| Pass | Hook | Publishes |
|------|------|-----------|
| Camera velocity | `MyRenderScheduler.Done` | `velocity` (RG16F). Composite keeps GBuffer MVs and camera-fills clear-zero pixels (sky / particles / foliage). |
| Linear depth + Hi-Z | Same Done, after velocity | `linearDepth`, `hiZ` — frozen for the rest of the frame |
| History color | `DrawGameScene` postfix, after debug overlay | `historyColor` (previous during this frame’s post) |
| Catalog debug | `DrawGameScene` postfix, `Priority.Last` | Nothing — overlay at `ViewportResolution` on the backbuffer, then `ClearState`. Velocity mode samples GBuffer depth (t1) so sky is dark grey. |

## Pack slots

| Slot | When | Use |
|------|------|-----|
| AfterLighting | Prefix `Transparent.Render` | HDR after lights, before atmosphere |
| AfterAtmosphere | Postfix `Atmosphere.RenderGBuffer` (after unbind) | Additive curtains. `Fullscreen/` uses the fixed bus; C# callbacks may set t20–t25. |
| AfterTransparent | Postfix `Transparent.Render` | After OIT + top billboards |
| BeforeTonemap | Prefix `ToneMapping.Run` (Last) | HDR grade, internal res |
| AfterTonemap | Postfix `Run` (First) | Internal LDR, before SE-DLSS evaluate |
| AfterUpscale | `NotifyUpscaleComplete(rc, color)` or `DrawGameScene` fallback | Output res. Read `upscaledColor` / `ctx.SceneColor`, not raw `LBuffer` |

```csharp
// ClientPlugin.Shaders.OwnedPassRegistry
Register("my.aurora", "AfterAtmosphere", 0,
    /* InColor|ContributeVelocity|Reactive */ 1 | 2 | 4,
    ctxObj => { /* OwnedPassContext */ });

// Display tenant (HdrRender-class). AfterUpscale reads upscaledColor.
Register("hdr.tonemap", "AfterUpscale", 0,
    /* InColor|Display */ 1 | 8,
    ctxObj => { /* ctx.SceneColor at ctx.Width x ctx.Height */ });
```

Upscalers call `ClaimUpscale("se-dlss")` at init and `NotifyUpscaleComplete(rc, dest)` after evaluate. Query `HasDisplayTenant` to evaluate pre-tonemap HDR. Display tenants query `HasUpscaleConsumer` and yield `MyToneMapping.Run`.

## TemporalPolicy

| Flag | Meaning |
|------|---------|
| InColor | Writes LBuffer (HDR) or LDR after tonemap |
| ContributeVelocity | Call `ctx.ContributeVelocity(overlay, mask)` — republishes velocity |
| Reactive | May write `reactiveMask` (R8, cleared to 0). High = reject history |
| Display | AfterUpscale display-referred grade. Sample `ctx.SceneColor` / `upscaledColor` |

> **Caution — Atmosphere inject does not fix DLSS.** Velocity freezes at scheduler Done. Animated emission after that is color-in / motion-out unless you contribute MVs and/or write the reactive mask. The unique upscaler must bind `reactiveMask` itself. When a Display tenant is registered, evaluate HDR and publish the dest — AfterUpscale is the clock, not the image.

> **Warning — Do not copy with `MyCopyToRT.Run`.** Other plugins may intercept that blit. History uses Anomaly’s `HistoryCopy.hlsl`. MSAA LBuffer is `ResolveSubresource`’d first.

→ [[Fullscreen-programs|Fullscreen programs]] · [[Frame-graph|Frame graph]] · [[Buffer-catalog|Catalog names]]
