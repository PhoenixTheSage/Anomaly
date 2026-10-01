# Owned passes

Two layers: Anomaly-drawn fullscreen products, and a scheduler packs register into. Resolve `ClientPlugin.Shaders.OwnedPassRegistry`. Anomaly owns the Harmony. Packs do not patch atmosphere or tonemap. Prefer [[Fullscreen-programs|Fullscreen programs]] (`Fullscreen/<Slot>/*.hlsl`) so the pack does not own a draw. Order at each slot: BeforeFullscreen C# → data-driven Fullscreen → AfterFullscreen C# (default).

On a GPU hang, search `SpaceEngineers.log` for `Anomaly RenderTrace dump at`. That line lists the last owned slots and fullscreen program ids Anomaly queued (`>` begin, `<` end). It does not name the hung HLSL by itself.

## Anomaly products

| Pass | Hook | Publishes |
|------|------|-----------|
| Camera velocity | `MyRenderScheduler.Done` | `velocity` (RG16F). Composite keeps GBuffer MVs and camera-fills clear-zero pixels (sky / particles / foliage). |
| Linear depth + Hi-Z | Same Done, after velocity | `linearDepth`, `historyDepth` (unread ping-pong), `hiZ` — frozen for the rest of the frame |
| Scene mip chain | AfterLighting, before Fullscreen programs | `litMips` — request-driven GenerateMips of this-frame `LBuffer` |
| Point-light shadows | AfterLighting, after BeforeFullscreen / `litMips` | `occupancy` + `pointShadowAtlas` when `RequestOccupancy` / `RequestPointShadows`. Skinned local-character mesh always; AABB boxes when `RequestWorldBoxes`. Default cube cap 4, face 128; max 64 / 256. |
| History color | `DrawGameScene` postfix, after debug overlay | `historyColor` (previous during this frame’s post) |
| Catalog debug | `DrawGameScene` postfix, `Priority.Last` | Nothing — overlay at `ViewportResolution` on the backbuffer, then `ClearState`. Velocity mode samples GBuffer depth (t1) so sky is dark grey. |

## Pack slots

AfterAtmosphere exits through a `finally` that restores Keen's vertex/pixel
frame constants and standard/shadow pixel samplers on the same context, including
failed tenants. Billboard soft-particle shaders inherit pixel b0 and samplers;
they must not receive the isolated contributor's cleared b0 or fullscreen bus
samplers. Restoration uses Keen wrappers to keep the deferred state cache valid.

| Slot | When | Use |
|------|------|-----|
| AfterLighting | Prefix `Transparent.Render` | HDR after lights, before atmosphere. Skipped on LCD / TargetView / TargetCamera. |
| AfterAtmosphere | Postfix `Atmosphere.RenderGBuffer` (after unbind) | Additive curtains. `Fullscreen/` uses the fixed bus; C# callbacks may set t20–t25. Records on Keen’s transparent deferred worker — use `ctx.Rc` only, never `MyRender11.RC`. Skipped on those hijacked views. |
| AfterTransparent | Postfix `Transparent.Render` | After OIT + top billboards. Skipped on those hijacked views. |
| BeforeTonemap | Prefix `ToneMapping.Run` (Last) | HDR grade, internal res. Skipped on those hijacked views. |
| AfterTonemap | Postfix `Run` (First) | Internal LDR, before SE-DLSS evaluate |
| AfterUpscale | `NotifyUpscaleComplete(rc, color)` or `DrawGameScene` fallback | Output res after an upscaler. Display-without-upscale grades `LBuffer` into the dest at `ResolutionI`. Read `upscaledColor` / `ctx.SceneColor`, not raw `LBuffer` at output size |

```csharp
// ClientPlugin.Shaders.OwnedPassRegistry
Register("my.aurora", "AfterAtmosphere", 0,
    /* InColor|ContributeVelocity|Reactive */ 1 | 2 | 4,
    ctxObj => { /* OwnedPassContext */ });

// BeforeFullscreen: publish catalog textures the PS will sample.
Register("ssgi.uniforms", "AfterLighting", 0, 1, ctxObj => { /* SetUniforms / SetEnabled */ }, /* BeforeFullscreen */ 0);

// Display tenant (HdrRender-class). AfterUpscale reads upscaledColor.
Register("hdr.tonemap", "AfterUpscale", 0,
    /* InColor|Display */ 1 | 8,
    ctxObj => { /* ctx.SceneColor at ctx.Width x ctx.Height */ });
```

Upscalers call `ClaimUpscale("se-dlss")` at init and `NotifyUpscaleComplete(rc, dest)` after evaluate. Query `HasDisplayTenant` to evaluate pre-tonemap HDR. Anomaly captures bloom / avg luminance / dirt on `MyToneMapping.Run` and skips Keen SDR when `HasDisplayTenant && !HasUpscaleConsumer`. The skip still returns a dest (`DrawGameScene.Tonemapped`, wrapped to `R16G16B16A16_Float` when Keen’s custom texture is 8-bit UNORM); AfterUpscale grades `LBuffer` into it before `DrawGameScene` copies. If the unique upscaler skips `Run` (Harmony prefix `false`) without setting `__result`, Anomaly’s postfix adopts the notified dest so `DrawGameScene` does not NRE on `.Linear` / `.SRgb`. BeforeTonemap still runs when that skip happens. FXAA / chromatic dests get the same wrap so values above 1 survive. Display HLSL samples t4–t6; C# callbacks still read `ctx.SceneColor`.

## TemporalPolicy

| Flag | Meaning |
|------|---------|
| InColor | Writes LBuffer (HDR) or LDR after tonemap |
| ContributeVelocity | IsolatedAdd reconstructs MVs from isolated.a (hit meters). C# may still call `ctx.ContributeVelocity(overlay, mask)` |
| Reactive | IsolatedAdd (and IsolatedMix / DirectAdd / PublishOnly) stamp dilated luma into `reactiveMask` on the slot’s `rc`. C# may still write the RTV via `ctx.Rc`. High = reject history |
| Display | AfterUpscale display-referred grade. Sample `ctx.SceneColor` / `upscaledColor`; t4–t6 are Keen bloom / avgLum / dirt |

> **Caution — Atmosphere inject does not fix DLSS.** Velocity freezes at scheduler Done. Animated emission after that is color-in / motion-out unless you contribute MVs and/or write the reactive mask. The unique upscaler must bind `reactiveMask` itself. When a Display tenant is registered, evaluate HDR and publish the dest — AfterUpscale is the clock, not the image.

> **Warning — Transparent slots are a deferred worker.** `MyTransparentRendering.DoWork` records AfterLighting / AfterAtmosphere / AfterTransparent on `AcquireRC("MyTransparentRendering")`, then `ConsumeWork` executes that list on the immediate context. Draw, clear, and stamp only on `ctx.Rc`. During those callbacks Anomaly redirects `MyRender11.RC` and `Device.ImmediateContext` to the slot `rc` and logs once — do not rely on that. Touching the real immediate context from that worker is a later `DEVICE_HUNG` at Present (Aurora IsolatedAdd + Reactive was the first tenant).

> **Warning — Do not copy with `MyCopyToRT.Run`.** Other plugins may intercept that blit. History uses Anomaly’s `HistoryCopy.hlsl`. MSAA LBuffer is `ResolveSubresource`’d first.

> **Warning — Keen `DrawFullscreenQuad()` resets the viewport.** With no `customViewport` it calls `SetScreenViewport()`. A half/quarter AfterFullscreen RT then stores only the top-left of UV 0–1, so screen-space lighting stretches with quality. Use `FullscreenPassRegistry.DrawFullscreen(rc, width, height)` (or `new MyViewport(rtWidth, rtHeight)`). Composite into `LBuffer` keeps dest size.

→ [[Fullscreen-programs|Fullscreen programs]] · [[Frame-graph|Frame graph]] · [[Buffer-catalog|Catalog names]]
