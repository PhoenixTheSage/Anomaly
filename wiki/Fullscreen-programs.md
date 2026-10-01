# Fullscreen programs

Anomaly owns the draw for pack pixel shaders under `Fullscreen/`. Overlay/Inject compose Keen **source**. Fullscreen composes **draws**. Packs do not call `Draw`, create RTs, or Harmony-patch Keen pass methods.

Resolve `ClientPlugin.Shaders.FullscreenPassRegistry`. Order at each slot: C# `OwnedPassPhase.BeforeFullscreen`, then data-driven programs, then AfterFullscreen C# (the default `Register`).

## Pack layout

### Isolated temporal distance

`FullscreenPassRegistry.SetVelocityDistanceScale(id, metresPerAlphaUnit)` returns
bool and defaults to 1 (raw metres). Finite values in [1, 1,000,000] are accepted
before/after registration and retained across reload/device release. Invalid
calls return false without changing the scale. For `IsolatedAdd` with
`ContributeVelocity`, write `hitMetres / scale` into alpha; the host decodes it
before camera reprojection. Clear alpha for empty volume emission or surface-only
glow to retain geometry motion. Invalid/nonpositive decoded distance or invalid
history also retains base motion. RGB composition is unchanged.

Reflection-bound packs must use scale 1 when this optional API is missing or
rejects their request. Aurora negotiates 1000, representing kilometres in FP16
alpha. This is independent of `SetScale`, which sizes the render target.
`IsolatedMix` alpha is opacity; do not encode distance into that alpha. A separate
distance product for simultaneous opacity and motion remains future work.

### Files

```
Fullscreen/
  AfterAtmosphere/Curtain.hlsl
```

Folder defaults: compose `IsolatedAdd`, id `{packId}.{name}`, output `pass.{id}`, temporal `InColor`, priority from the pack, scale `1`. Skip `.hlsli`. Unknown slot fail closed. Shared helpers belong in `Inject/` so the compile include path sees them.

`passes[].scale` / `FullscreenPassRegistry.SetScale(id, scale)` sizes the isolated RT to **1**, **0.5**, or **0.25** of the slot’s scene size (`ResolutionI`, or `ViewportResolution` at AfterUpscale). Values snap to those three. **Replace ignores scale** (always dest-sized). `passes[].history` names a catalog RT. After that program draws, Anomaly copies its output there when the size matches (Slice AV). The pack publishes the RT before the slot. A missing or same-resource target skips the copy. Merge upsamples by UV. `pass.<id>` publishes the scaled size. GBuffer / `linearDepth` / `velocity` stay full-res — sample them by UV (`AnomalyScenePixel(uv)`), not `SV_Position`, except AfterLighting IsolatedSub of tiled point lights, which must reconstruct like Keen `LightPoint.hlsl` (`AnomalyLightingUv(SV_Position)`, `AnomalyLightingViewPos`, `AnomalyLightingN`). Interpolator `TEXCOORD` ignores `Frame.Screen.offset`. Convert a full-res pixel radius to UV with `AnomalySceneUvOffset` (`AnomalyInvSceneSize`), not `AnomalyInvPassSize`. `AnomalyLightingRenderSize` / `AnomalyPassSize` is the pass RT; `AnomalySceneSize` is full-res. Chain members must share a scale. Status shows `@1/2` or `@1/4` when scaled. AfterFullscreen C# that draws the scaled `pass.<id>` (SSGI SVGF) must call `FullscreenPassRegistry.DrawFullscreen(rc, width, height)` — Keen `DrawFullscreenQuad()` with no viewport calls `SetScreenViewport()`.

`anomaly.json` `passes[]` overrides those defaults. Two json passes may share one hlsl with different ids (IsolatedSub + debug Replace). The first json pass for that file adopts the folder-scan id; later ids add siblings. Matching by file used to overwrite IsolatedSub.

```json
{
  "passes": [
    {
      "id": "example.curtain",
      "slot": "AfterAtmosphere",
      "file": "Fullscreen/AfterAtmosphere/Curtain.hlsl",
      "compose": "IsolatedAdd",
      "priority": 0,
      "temporal": ["InColor", "ContributeVelocity"],
      "output": "pass.example.curtain",
      "scale": 1
    }
  ]
}
```

## Compose

| Mode | Who | Dest |
|------|-----|------|
| IsolatedAdd (default) | Many, additive | Scratch then `src.rgb + dest.rgb` (isolated.a is pack data, not merged) |
| IsolatedMix | Many, over | Scratch then `src + dest * (1 - src.a)`. `src.rgb` is **LBuffer energy**, not 0–1 albedo. High `a` + dim `rgb` replaces HDR sky. High RGB + low `a` is fireflies. Light with `AnomalySunColor * AnomalySunDiffuse`. In-cloud day fill is `AnomalyVolumeAmbient()`. Planet-night is `AnomalyVolumeNight` (AJ × 0.02). Sun vis is `AnomalySunTransmittance` (monotonic squared limb). `passes[].scale` is pixel cost, not lighting. |
| IsolatedSub | Many, occlude | Scratch then blend `dest.rgb * (1 - saturate(src.rgb))` onto dest (same dest RTV as Replace). Pack writes a **0–1 dest fraction** (`AnomalyIsolatedSub` / `AnomalyIsolatedSubEnergy`). IsolatedSub blits dest for t0 when dest aliases LBuffer. `temporal` `InColor`+`Reactive` stamps `.a` — not `ContributeVelocity` |
| Chain | Many, ordered | Each samples the previous isolated; last copies to dest |
| PublishOnly | Producer | Scratch only; catalog `pass.<id>` or a conventional name such as `volumeSunShadow` (not reserved) |
| Replace | One owner | Dest when t0 is a different resource (optional `__compute_shader` UAV). Scratch+copy only when dest aliases t0 (DLSS in-place). Two live Replaces fail closed. A pack-disabled Replace does not fail-close Isolated siblings; while a Replace is live, only Replace draws that frame |
| DirectAdd | Opt-in | Isolated then additive merge |

HDR slots (AfterLighting / AfterAtmosphere / AfterTransparent / BeforeTonemap) merge into `LBuffer`. AfterTonemap merges into Keen’s tonemap result. AfterUpscale t0 is catalog `upscaledColor` when the unique consumer notified with a dest. Replace draws into that dest when it is a different GPU resource from t0; Isolated still publishes. When dest **is** t0 (DLSS evaluate-in-place), Anomaly grades into UAV scratch (compute when `__compute_shader` compiled) then copies so `__result` stays graded and there is no RTV+SRV hazard. Display-without-upscale grades native `LBuffer` at `ResolutionI`. Without a dest, t0 falls back to `LBuffer` (wrong at output res).

## Bus (fixed)

`#include <AnomalyFullscreen.hlsli>`

| Slot | What |
|------|------|
| t0 | Scene color (`LBuffer`, LDR dest, `upscaledColor` at AfterUpscale, or previous isolated in Chain) |
| t1 | `linearDepth` |
| t2 | `velocity` |
| t3 | `reactiveMask` |
| t4–t6 | HDR slots: Keen `GBuffer0` / `GBuffer1` / `GBuffer2`. AfterTonemap / AfterUpscale: `avgLuminance` / `bloom` / `dirt` |
| t7–t9 | Pack catalog extras (`RequestSrv` or json `passes[].binds`). Default HLSL type is `Texture2D` (`AnomalyPackSrv0–2`). `#define ANOMALY_PACK_SRVn_TYPE Texture3D` **before** `#include <AnomalyFullscreen.hlsli>` to bind a 3D volume on that slot. |
| t12 / t13 | Pack SVO extras (`AnomalyPackSrv3` / `AnomalyPackSrv4`). Default `Texture3D`. `#define ANOMALY_PACK_SRV3_TYPE StructuredBuffer<uint4>` for a node pool. t10/t11 stay tiled lights. |
| t10 / t11 (HDR slots only) | Keen tiled point lights: `StructuredBuffer<AnomalyPointLight> AnomalyPointLights` / `StructuredBuffer<uint> AnomalyTileIndices`. Dummy 1-element SRVs when catalog empty. |
| b0 (HDR slots only) | Keen `MyCommon.FrameConstants` (`frame_` from `Frame.hlsli`, included by `AnomalyFullscreen.hlsli`). `tiles_x` / `tiles_num` / `maxTileLights` / `aoPointLight` match `LightPoint.hlsl`. |
| s0 / s1 / s2 | Point / Linear (clamp) / `CloudSampler` wrap (`AnomalyWrapSampler`) |
| b6 | Extras **320 B**: pass size (`AnomalyLightingRenderSize` / `AnomalyPassSize`), full-res `AnomalySceneSize` / `AnomalyInvSceneSize`, jitter, unjittered VP, prev VP, frame, `AnomalyCameraToWorld` (3×4), `AnomalyProjScale` (proj M11/M22), `AnomalySafetyScale` (1 = calm), `AnomalySunColor` / `AnomalySunDiffuse` / `AnomalySunToward` / `AnomalySkyLuma` / `AnomalySkyAmbient` / `AnomalyPlanetAirTop` / `AnomalyVisualAtmoCeil` / `AnomalyVolumeSkipFloor` / `AnomalyVolumeSkipMul` (Slice AS; 0 / ≤1 fail closed to 0.38 / 6) |
| b7 | `AnomalyPassUniform0–15` from `FullscreenPassRegistry.SetUniforms(id, float[64])`. `0–7` are the original 128 B; `8–15` append to 256 B. Extras on b6 are 320 B. Longer arrays fail closed; Anomaly logs once per id. |

Do not steal atmosphere t5. AfterAtmosphere runs after Keen unbinds `DensityLut`. Display AfterUpscale programs sample t4–t6 tonemap inputs; HDR slots sample GBuffer there instead. Pack extras land at t7–t9 and t12–t13.

C# extra SRVs:

```csharp
t?.GetMethod("RequestSrv")?.Invoke(null, new object[] { "example.curtain", "litMips", 7 });
t?.GetMethod("SetEnabled")?.Invoke(null, new object[] { "example.curtain", enabled });
t?.GetMethod("SetScale")?.Invoke(null, new object[] { "example.curtain", 0.5f });
t?.GetMethod("TryGetProgramStatus")?.Invoke(null, new object[] { "example.curtain", null });
```

`SetEnabled(id, false)` is the pack checkbox. It does not override two-Replace fail-closed. Status shows a trailing `-` when the pack disabled the program. Compile failures stay visible as `id!compile` (Anomaly.debug.log has the FXC line). Pack Status dialogs call `TryGetProgramStatus(id, out string)`. Draw runs only when both the conflict flag and the pack flag are true. A live Replace is the only compose drawn on that slot that frame — Isolated siblings stay `Enabled` so they resume when the Replace is pack-disabled. `SetScale` survives reload the same way.

Catalog `litMips` is generated at AfterLighting when a live program bound that name (json `binds` or `RequestSrv`), `BufferCatalog.RequestLitMips(mipLevels)` was called, or Debug view is LitMips. Default 5 mips of this-frame `LBuffer` (before atmosphere). Packs do not `GenerateMips` themselves.

`BufferCatalog.RequestOccupancy()` / `RequestPointShadows(maxLights, faceResolution)` fill `occupancy` and `pointShadowAtlas` at AfterLighting after BeforeFullscreen. Cube cap default **4**, max **64**; VRAM grows with the requested cap, not the max. `historyDepth` is the unread linear ping-pong (no request). Bind them on t7–t9. SVO node/brick catalogs bind on t12–t13.

## Temporal

Folder default is `InColor`. After `Scheduler.Done` that is color-in / motion-out unless you also set `Reactive` and/or `ContributeVelocity`. IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly with `ContributeVelocity` reconstruct camera MVs from isolated.a (view-space hit distance in meters; 0 = no overlay) and composite them over catalog `velocity` so DLSS can lock the curtain instead of using far-plane sky parallax. IsolatedAdd RGB merge does not write that alpha into `LBuffer`. IsolatedAdd / IsolatedMix / IsolatedSub / DirectAdd / PublishOnly with `Reactive` stamp into `reactiveMask` (2px, IsolatedAdd luma × 8, IsolatedSub `.a`, max with prior stamps) on the slot’s `rc` — AfterAtmosphere is Keen’s transparent deferred worker. IsolatedSub umbra uses `Reactive` (history reject), not `ContributeVelocity`. Anomaly clears/stamps on that `rc` and redirects `MyRender11.RC` during AfterLighting / AfterAtmosphere / AfterTransparent callbacks. Anomaly logs once per program id (`motion-out`). Atmosphere inject still does not invent motion vectors. Prefer `ContributeVelocity` (packed hit `t`) over `Reactive` when the curtain should reconstruct; `Reactive` is a history reject and flickers under spectator translation. C# may still call `ContributeVelocity(overlay, mask)`.

## Camera safety

`FrameTemporal.SafetyScale` is camera move / look (1 = calm, 0 ≈ 80 m cut). It is written to extras as `AnomalySafetyScale` every fullscreen and lighting extras upload. Calm frames write `1` — same ALU as the old pad. A slam drops the scale this frame (TDR); recovery is limited to `1/16` per frame so spectator speed chatter cannot oscillate march step counts (GPU hitch locked to undersampling grain).

March packs should call `AnomalyMarchSteps` with a **uniform** camera-to-volume distance. Do not globally floor SafetyScale — that keeps a heavy march during a slam and re-opens the dive TDR. Distant slam still keeps a few steps so the volume does not vanish (pack hit `t` + `ContributeVelocity`). Per-ray hit distance is the wrong cheapening metric: grazing chords keep the compile-time max and a warp runs that max. Fade detail as the camera leaves the volume with `AnomalyVolumeViewLod(camToVolumeMeters, farMeters)` (0 at the shell, 1 at `farMeters`) — do not invent a binary orbit flag. Night-side lighting is `AnomalySunVisibility(pos, center, occluderRadius, lightWrapMeters)` (local-up × sun, wrap = atmosphere thickness **capped at 12% of radius**); do not sample Keen shadow cascades from AfterAtmosphere — they do not cover a planet disk from orbit.

```hlsl
int steps = AnomalyMarchSteps(stepBudget, 4, MAX_STEPS, distToVolume, 0.0, max(thickness * 2.0, 1e-5));
float stepDt = AnomalyVolumeSkipDt(occupancy, dt, max(dt, 480.0)); // Slice AS; thin-slab cap
t = AnomalyVolumeAdvance(t, t1, occupancy, dt, max(dt, 480.0));
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

Debug buffer **FullscreenIsolated** overlays the last isolated output. Show Status lists `Fullscreen: slot/compose:id` (a trailing `-` means `SetEnabled(false)`; `id!compile` means FXC failed). HDR slots skip LCD / TargetView / TargetCamera.

→ [[Owned-passes|C# slot register]] · [[Overlay-vs-inject|Source compose]] · [[HLSL-cookbook|PS snippet]]
