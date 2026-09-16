# Pick a path

Three jobs share one plugin. Start on the page that matches what you will ship this week.

## I want to change how Keen shades

*Most pack authors*

Ship a Pulsar plugin that depends on Anomaly. Prefer `Inject/` so velocity and other extras stay alive. Overlay a named stage only when you must replace a whole program. Never fork `Materials/Standard/Pixel.hlsl` for a small extra.

→ [[Your-first-pack|Scaffold a pack]] · [[Overlay-vs-inject|Inject vs overlay]] · [[Named-stages|Stage names]] · [[Fullscreen-programs|Fullscreen programs]] · [[Terminal-config|Terminal page]]

## I want settings in the Rich HUD terminal

*Pack or consumer options*

Request a page on `ClientPlugin.RichHud.TerminalConfigRegistry`. It appears under **Anomaly Shaders** beside the **Anomaly** folder when Master is in the world. Corner status goes on `HudOverlayRegistry.Register` (cached string, no disk I/O). Pulsar MyGui still works without Master. Do not vendor a second Rich HUD client.

→ [[Terminal-config|Terminal config]] · [[Your-first-pack|Register from LoadAssets]]

## I want a fullscreen effect Keen does not draw

*Aurora, SSGI, Volumetric Clouds, Screen Space Shadows*

Drop `Fullscreen/<Slot>/*.hlsl`. Anomaly owns `Draw(3)`, the scratch pair, and the merge. `SetEnabled` is the pack checkbox so Trace does not run when SSGI is off. `SetScale` / `passes[].scale` (1 / 0.5 / 0.25) sizes the isolated RT — sample GBuffer and depth by UV. Catalog `litMips` is an Anomaly product. Optional C# AfterFullscreen owned passes cover loops Anomaly cannot express (SSGI SVGF). Those draws must use `FullscreenPassRegistry.DrawFullscreen(rc, passW, passH)` — Keen `DrawFullscreenQuad()` with no viewport is screen-sized. Publish pack textures; do not hijack Keen’s compiler or Target3.

→ [[Fullscreen-programs|Fullscreen programs]] · [[Owned-passes|C# escape hatch]]

## I want a texture Keen does not publish

*SE-DLSS, TAA, SSR*

Bind the catalog. Do not generate a second motion-vector buffer. Do not patch instance updates. If Anomaly is missing, keep your own fallback.

→ [[Buffer-catalog|Named buffers]] · [[Velocity-contract|Velocity flags]] · [[Owned-passes|When they exist in the frame]]

## I am changing Anomaly itself

*Framework work*

One compile intercept. Depth stays 3-attachment-free. Extra GBuffer targets are requested, not spliced by hand. Ground rules live on [[Composition-rules|Composition rules]]. When a pack invents lighting, compose energy, or march LOD that the next shader will also need, file it on [[Framework-gaps]] in the same turn.

→ [[How-the-frame-works|Four layers]] · [[Composition-rules|Rules]] · [[Framework-gaps|Open gaps]]
