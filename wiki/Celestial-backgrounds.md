# Celestial backgrounds

The initial `ClientPlugin.Shaders.CelestialBackgroundRegistry` contract is implemented for a single provider, before atmosphere and in environment-probe backgrounds. It supplies copied live uniforms, immutable float4 data, view-specific direction/footprint, paired shader compilation and vanilla fallback. Foreground lighting and gameplay sunlight are unchanged.

See [the full contract](https://github.com/PhoenixTheSage/Anomaly/blob/main/Docs/CelestialBackgrounds.md). FinalFrontier is the first tenant and currently draws diagnostic stars and an analytic disc. MSAA explicitly retains vanilla; sun glare ownership and projected labels are deferred. Game integration and temporal-upscaler visual validation are still pending.

`HudOverlayRegistry` remains a corner-text API. Constellation guides should not be mistaken for an already-available projected HUD API.
