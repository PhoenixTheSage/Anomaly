# Pick a path

Three jobs share one plugin. Start on the page that matches what you will ship this week.

## I want to change how Keen shades

*Most pack authors*

Ship a Pulsar plugin that depends on Anomaly. Prefer `Inject/` so velocity and other extras stay alive. Overlay a named stage only when you must replace a whole program. Never fork `Materials/Standard/Pixel.hlsl` for a small extra.

→ [[Your-first-pack|Scaffold a pack]] · [[Overlay-vs-inject|Inject vs overlay]] · [[Named-stages|Stage names]] · [[Fullscreen-programs|Fullscreen programs]]

## I want a fullscreen effect Keen does not draw

*Aurora-class curtains, grades, veils*

Drop `Fullscreen/<Slot>/*.hlsl`. Anomaly owns `Draw(3)`, the scratch pair, and the merge. Optional C# writes `SetUniforms` or `TemporalPolicy`. Do not create pack RTs.

→ [[Fullscreen-programs|Fullscreen programs]] · [[Owned-passes|C# escape hatch]]

## I want a texture Keen does not publish

*SE-DLSS, TAA, SSR*

Bind the catalog. Do not generate a second motion-vector buffer. Do not patch instance updates. If Anomaly is missing, keep your own fallback.

→ [[Buffer-catalog|Named buffers]] · [[Velocity-contract|Velocity flags]] · [[Owned-passes|When they exist in the frame]]

## I am changing Anomaly itself

*Framework work*

One compile intercept. Depth stays 3-attachment-free. Extra GBuffer targets are requested, not spliced by hand. Ground rules live on [[Composition-rules|Composition rules]].

→ [[How-the-frame-works|Four layers]] · [[Composition-rules|Rules]]
