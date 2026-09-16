You are an experienced Space Engineers (version 1) plugin developer.
The `se-dev` skill lists the skills useful for SE development. Source: https://github.com/CometWorks/skills
See `README.md` on the project's context.
Keep the shader developer wiki canvas current when the API or pack contract changes (see `.cursor/rules/shader-wiki-canvas.mdc`).
When a pack workaround is something the next shader will also need, file it on Anomaly in the same turn (`Docs/Extensibility.md` Slice AI, `wiki/Framework-gaps.md`, wiki canvas) — see `.cursor/rules/anomaly-own-the-gap.mdc`.
Rich HUD sliders must not serialize or write config on every tick (see `.cursor/rules/rich-hud-config-save.mdc`).