# Implementation roadmap

Ordered work to turn the compile-and-load stub into a living shader framework. Architecture: [ShaderAPI.md](ShaderAPI.md). Product phases and Keen facts: [PLAN.md](PLAN.md). Shader inventory: [KeenShaders.md](KeenShaders.md).

**Now:** Slices A–E are implemented. Compile intercept, camera `IVelocityBuffer`, ActorID history, GBuffer `SV_Target3` piggyback, previous bones, clipmap camera-only, and a never-zero camera composite are in. Default `VelocitySource` is `GBuffer`.

**Next:** Slice F — shader pack registry (named Pulsar assets, `ShaderPackRegistry.Register`, overlay path rules). Then Slice G in the SE-DLSS repo.

---

## Docs map

| Doc | Role |
|-----|------|
| [PLAN.md](PLAN.md) | Why velocity, Keen transforms, product phases 0–7, tests |
| [ShaderAPI.md](ShaderAPI.md) | Hook / inject / replace / owned-pass layers; Iris comparison |
| [ShaderPacks.md](ShaderPacks.md) | Pulsar named assets; pack plugins register HLSL with Anomaly |
| [KeenShaders.md](KeenShaders.md) | All Keen HLSL files and GBuffer layout |
| This file | What to implement next, in order |

---

## Ground rules (do not skip)

- Windows, Pulsar, Harmony + publicizer + `VRage.Render11` only. No NGX in this repo.
- Do not widen Keen’s 64-byte instance VB. Do not hook `MyInstance.UpdateWorldMatrix`.
- Inject **GBuffer only**. Depth must stay 3-attachment-free.
- Consumers resolve `IVelocityBuffer` by type name; no compile-time reference to Anomaly.
- Unbind extra RT/SRV before returning to Keen (Rich HUD).
- SmoothFrames may also patch the render thread; do not assume exclusive `DrawGameScene`.
- Prefer include injection into shared stages over forking `Materials/*`.

---

## Slice A — Compile intercept (start here)

Goal: Anomaly can add an include directory and a define on **every** Keen permutation compile. Cache still hits. Depth still compiles.

### A1. Finish Phase 0 hygiene

The tree may still contain leftover SE-DLSS sources (`ClientPlugin/Dlss/`, DLSS-oriented `Patches/`). Before new hooks:

- [x] Confirm `Plugin.Init` does not patch NGX / AA / jitter / DRS from DLSS leftovers. `harmony.PatchAll` will apply **every** `[HarmonyPatch]` in the assembly.
- [x] Delete leftover DLSS patches so they cannot run. Only `Patches/ShaderCompilerPatch.cs` is Harmony.
- [x] Confirm `Native/SeDlssNgx` is not part of the ClientPlugin build.

### A2. Find Keen’s compile entry

Search `VRage.Render11` (se-dev-game-code / ILSpy). Record the type + method in this file or a short comment on the hook class.

**Entry:** `VRageRender.MyShaderCompiler` (`VRage.Render11.dll`).

- Source path: `Path.Combine(ShadersPath, info.File)` where `ShadersPath` = `MyFileSystem.ShadersBasePath` + `"Shaders"` (`Content/Shaders`).
- Defines: per-permutation `ShaderMacro[]` plus private `GlobalShaderMacros` (PC starts empty). 11-arg `Compile` copies globals then permutation macros then `FillGlobalMacros`.
- Includes: private `List<string> m_includes`, default `{ ShadersPath }`. `MyIncludeProcessor.Open` searches this list **from the end** for `#include <…>` (system). Local `"…"` includes with a base path do **not** fall through if missing — overlays must use `<Anomaly.hlsli>`.
- Cache key: `MyShaderCache.GetShaderHash(preprocessedSource, profile)`. Unused macros (`ANOMALY` with no `#ifdef` in Keen files) do not change preprocess text, so the Keen cache still hits. Later overlays that `#include` Anomaly will miss, correctly.

Need to know:

- [x] Where the HLSL source path is resolved
- [x] Where `#define` / permutation flags are assembled
- [x] Where include directories are passed to D3DCompiler
- [x] How the cache key is built (must include Anomaly defines + overlay identity later)

### A3. Hook: include dir + define

- [x] Harmony postfix on `Includes`, `GlobalShaderMacros`, and 5-arg `Compile` (`ClientPlugin/Patches/ShaderCompilerPatch.cs`). `ShaderCompileIntercept.Activate` also writes `m_includes` / `m_globalShaderMacros` directly.
- [x] Append Anomaly’s shader folder: `LoadAssets` + `Assets/Shaders`, with `assemblyDir/Shaders` fallback (Deploy copies the folder next to the DLL).
- [x] Define `ANOMALY=1` always once the hook is live; `ANOMALY_VELOCITY` only when velocity inject is on (later). This slice is a no-op define.
- [x] Log compile failures (null/empty bytecode) with descriptor + macros to `SpaceEngineers.log` + Anomaly debug log.

Do **not** rewrite Keen files on disk. Do **not** `#include` Anomaly from Keen files in this slice (would bust the shader cache).

### A4. Prove it in-game

- [x] Status line: `Compile intercept: live` (plus include path, `ANOMALY=1`, compile/fail counts).
- [ ] Load a world with blocks, voxels, glass, a character (skinning), shadows.
- [ ] Standard **GBuffer** permutations compile.
- [ ] **Depth** permutations compile (shadows still work). This is the ship-blocker for the whole inject design.
- [ ] Shader cache survives restart (no recompile storm unless defines changed). Expect **no** storm: unused `ANOMALY` does not change the preprocess hash.

**Slice A code done when:** intercept is on and Show Status says live. **Slice A proven when:** Depth still compiles in-game, no visual change.

---

## Slice B — Camera velocity as `IVelocityBuffer`

Goal: `VelocitySource.CameraOnly` publishes a real `RG16F` buffer. SE-DLSS can bind it later without Anomaly GBuffer inject.

HLSL already exists: `ClientPlugin/Shaders/CameraVelocity.hlsl` + `Fullscreen.hlsl`. Wire it.

- [x] Create/resize an `RG16F` RT at internal (`ResolutionI` / DRS) size. Follow device create, `SetDRS` (`CreateScreenResources`), dispose (`OnDeviceEnd`).
- [x] Bind Keen resolved depth (`MyGBuffer.Main.ResolvedDepthStencil.SrvDepth`); draw fullscreen with unjittered current + previous view-proj. **Anomaly owns these matrices** (zero `Projection.M31/M32`; do not depend on SE-DLSS `Jitter`).
- [x] Implement `IVelocityBuffer` (`CameraVelocityBuffer`: SRV, native resource, size, `VelocityConvention`, `HistoryValid` after the second frame / camera-cut reset).
- [x] `VelocityRegistry.SetActive` on this producer when the pass runs (fallback for GBuffer / OwnedRaster until those exist).
- [x] Status: size, convention, `HistoryValid`, `Camera velocity: live`.
- [x] Unbind RT/SRV after the pass.
- [ ] PIX/Nsight: static look-around looks like camera motion, not zeros.

**Slice B code done when:** Show Status reports a live buffer after a world is loaded. **Slice B proven when:** texture is inspectable in PIX.

---

## Slice C — Actor history (CPU)

Goal: previous absolute `MatrixD` by `ActorID` for both geometry pipelines. No HLSL load yet.

- [x] `IVelocityHistory` implementation: snapshot after `MyGeometryRenderer.UpdateMatrices` and old cull-proxy update. Key `ActorID` (`uint`).
- [x] Swap current → previous at `DrawGameScene` postfix. Drop ids not seen for N frames (3).
- [x] Teleport threshold (30 m translation) → treat as new actor (camera MV only next frame).
- [x] Status: history actor count.
- [x] Do not hook `MyInstance.UpdateWorldMatrix`. Do not key by VB slot.

**Slice C code done when:** Show Status `History actors` is non-zero in a world with grids/characters. **Slice C proven when:** count tracks visible movers; teleports reset.

---

## Slice D — GBuffer piggyback (success criterion)

Goal: object velocity on the same pixels Keen GBuffer accepted. Default source becomes `GBuffer`.

Depends on A (defines on GBuffer only) + B (camera fill) + C (prev world).

- [x] Extra `RG16F` (or `RGBA16F`) RT, render resolution, bound in `MyGBufferPass.Begin` as **fourth** color target. **Not** bound for Depth.
- [x] Define `ANOMALY_VELOCITY` **only** for GBuffer permutations.
- [x] Shared-stage HLSL (Anomaly overlay includes, not a Materials fork):
  - velocity interpolant in `GBuffer/VertexStage`
  - `SV_Target3` in `GBufferWrite` / GBuffer `PixelStage`
- [x] Side SRV: previous world 3×4 packed in Stage 2 / merge draw order (`SV_InstanceID`). Statics skip the load (camera term only).
- [x] First frame / new ActorID / teleport: camera MV, never a zero buffer.
- [x] Alpha / `GbufferWriteBlend` / `CUSTOM_DEPTH` / coverage: defined velocity (camera or discard).
- [ ] In-game: moving large grid no longer ghosts vs camera-only; static look-around no worse.
- [x] Status: `GBuffer injection: live`.

Skip [PLAN.md](PLAN.md) phase 4 (owned raster) unless D is blocked and SE-DLSS needs object MVs immediately.

**Slice D done when:** default `VelocitySource` is `GBuffer`; Depth still has no fourth target.

---

## Slice E — Coverage holes

[PLAN.md](PLAN.md) phase 6. After D is stable:

- [x] Previous bones (SRV, up to 60). Mismatch → camera fallback.
- [x] Voxel movers: ActorID + `LastWorldMatrix`. Clipmap rebuilds: camera only.
- [x] GPU particles, foliage, glass/holo/shield: camera only unless instance-backed.
**Slice E code done when:** Show Status reports `History bones` for a character and `GBuffer injection: live`. **Slice E proven when:** skinned movers no longer ghost vs rigid; sky/particles are not stuck at zero; Depth/shadows still work.

## Slice F — Shader API surface (after D)

Do not block velocity on this. Layer 0 already makes it possible. Pack contract: [ShaderPacks.md](ShaderPacks.md).

- [ ] Migrate `Anomaly.xml` to named `<Asset>` entries (`AssetFolder` reserved name + `Shaders`). Implement `LoadAssets(IReadOnlyDictionary<string, string>)` while keeping `LoadAssets(string)`.
- [ ] Use `assets["Shaders"]` as the compile-hook include directory (slice A can hardcode until this lands).
- [ ] `ShaderPackRegistry.Register(id, root)` — static, well-known type name. Packs call it from *their* `LoadAssets`; Anomaly `Init` consumes the list (instantiate order is not dependency-sorted).
- [ ] Pack root: `anomaly.json` + optional `Overlay/` (Keen-relative replace) + `Inject/` (additive includes).
- [ ] Restrict overlay paths (no `..`). One owner per replace key; Hub packs fail closed on conflict.
- [ ] Cache key includes overlay hash + define set + pack fingerprints.
- [ ] Optional local-only drop dir via Pulsar `GetConfigPath("Packs")` — not for PluginHub.
- [ ] Do **not** scan Pulsar internals for other plugins’ `GetNamedAssets()`.

---

## Slice G — SE-DLSS consumer (other repo)

Not this tree. [PLAN.md](PLAN.md) phase 7.

- [ ] Bind `VelocityRegistry.Active` when present; else camera-native.
- [ ] No second MV generate when External is live.
- [ ] Optional `<DependencyIds>` after Anomaly is on PluginHub.

---

## Suggested order on the calendar

Do A completely before B if time is short: a broken Depth compile is worse than no velocity.

| Order | Slice | PLAN phase | Layer ([ShaderAPI.md](ShaderAPI.md)) |
|------:|-------|------------|--------------------------------------|
| 1 | A compile intercept | 1 | 0 |
| 2 | B camera `IVelocityBuffer` | 2 | 3 |
| 3 | C actor history | 3 | — (CPU) |
| 4 | D GBuffer piggyback | 5 | 1 |
| 5 | E skinning / voxels | 6 | 1 |
| 6 | F overlay registry | — | 2 (stub) |
| 7 | G SE-DLSS bind | 7 | consumers |

Phase 4 owned raster: only if D is blocked.

---

## First implementation session (checklist)

Slice A–C code are in. Remaining proof is in-game:

- A4: shadows + blocks, Show Status `Compile intercept: live`
- B: live `RG16F` + `HistoryValid` after looking around
- C: `History actors` tracks visible movers; jumps reset

Next session is **slice F** (shader pack registry) after in-game proof of D/E.
