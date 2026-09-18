# Celestial backgrounds (initial contract)

Implemented 2026-09-18. This is a depth-masked background replacement at the start of `AfterLighting`, before fullscreen tenants, atmosphere and transparency, plus a probe background replacement immediately after Keen's `RunForwardPostprocess`. It does not fork `LightDir.hlsl`, replace foreground lighting, or change gameplay sunlight. Existing extras remain 320 bytes.

Resolve `ClientPlugin.Shaders.CelestialBackgroundRegistry` by name. Register from a Pulsar named asset path; no compile-time reference to Anomaly is required.

```csharp
bool Register(string id, string shaderFile);
void Unregister(string id);
void SetEnabled(string id, bool enabled);
void SetNativeSunGlare(string id, bool enabled); // default true; exact solar flare only
void SetUniforms(string id, float[] values); // 0–64 finite floats, zero-padded to 64
void SetData(string id, float[] values);     // float4 records; 0–4,194,304 finite floats
void Retry(string id);                     // retry compilation on next eligible draw
string StatusLine { get; }
```

Registration is idempotent for the same ID/path. More than one registered provider disables replacement for all providers; disabling a provider does not relinquish its claim. `Unregister` relinquishes it. Setters copy caller arrays under the registry's CPU synchronization and do no GPU work. Uniforms upload on the actual draw context. Data uses an immutable structured GPU buffer, uploaded only on change/device recreation. Shader compilation and resource creation are render-time operations. A compilation failure is latched until Retry, path replacement or device recreation; toggling is not a compile loop.

The provider HLSL includes `AnomalyCelestial.hlsli`, defines `float3 AnomalyEvaluateCelestial(AnomalyCelestialInput input)`, then includes `AnomalyCelestialEntry.hlsli`. Anomaly compiles the file for main and probe views before using either. Both native shader objects must exist before drawing. Supply complete linear HDR background radiance. No destination texture is exposed: foreground pixels are discarded and the background is replaced with opaque radiance.

Input fields: normalized world `direction` away from the camera, `directionDx`/`directionDy` (world-ray pixel derivatives evaluated before depth discard for integrated point-source profiles), `sunDirection`, `sunRadiance` (Keen disc color × directional color × disc intensity), approximate angular pixel width in radians, `isProbe`, and float4 `dataCount`. `CelestialUniform[16]` exposes b7. `AnomalyCelestialData` is a structured float4 buffer at t1; index only below dataCount. The host owns raw complementary depth at t0, FrameConstants at b0 and an 80-byte view CB at b6. These bindings belong only to this pass and are cleared afterward. They are not an extension to the existing general b6 extras layout.

Main-view stars, sun and guides deliberately share the scene projection, including FOV and projection offset. Camera translation is excluded from celestial direction. Probes use the supplied probe view rotation and projection scale, not the main-view camera. Derivative footprint is calculated before depth discard. The main pass reapplies Keen's sky fog; probe fog/atmosphere remains in its existing later pipeline. The provider decides probe contents: FinalFrontier excludes its sun disc to avoid blindly adding energy on top of directional specular. Guides must also be excluded.

No provider, disabled provider, missing probe hook, multiple providers, explicit `LightDir.hlsl`/`ForwardPostprocess.hlsl` pack overlays, MSAA, or a compile/resource failure retains the original background. Unsupported MSAA currently falls back in both main and probe views; mixed-sample edge preservation needs its own implementation. Arbitrary custom lighting injection is not automatically conflict-detectable. Non-finite shader output discards that pixel. Actual device loss is rethrown rather than hidden as an ordinary fallback. Existing probe caches update on Keen's schedule, so reflection changes/restoration may lag the main view.

Use `StatusLine` for a cached HUD status. It describes resource/provider state, not proof that every camera path or on-screen result is correct. Disable the provider to restore vanilla without recompilation. The initial provider leaves the vanilla sun flare intact. Sun-only glare isolation is available; analytic effects, partial-disc visibility and projected labels remain deferred to FinalFrontier phases 4/5. Native/MSAA/upscaler temporal behavior still needs in-game validation; the depth-zero velocity fallback has not been changed speculatively.

Validation: Anomaly builds on net48/net10.0; FinalFrontier's `Tests/Run-CelestialSmokeTests.ps1` compiles both actual provider variants against installed Keen headers and executes them with D3D11 WARP. The harness validates sky/foreground separation, sun alignment, probe sun exclusion, fog, main/probe direction agreement, translation invariance, narrow-FOV finite output, rotated probes and live uniforms. It does not initialize Keen's renderer or validate its Harmony hooks/deferred scheduling.

Sun-glare isolation (2026-09-18): SetNativeSunGlare(false) suppresses only the
exact MySector solar light during MyFlareRenderer.Draw, after a successful main
celestial draw in the current FrameTemporal frame. Disabled/unregistered,
conflicting or failed providers and device release restore the native path.
The solar render ID is cleared on sector unload. Other distant lights, ship
flares, light intensity and world flare definitions are unchanged. This hook
requires in-game validation; it is an isolation control, not an analytic corona.
