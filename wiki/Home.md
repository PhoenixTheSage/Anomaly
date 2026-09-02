# Anomaly shader developer wiki

Onboarding for people who write HLSL packs or bind Anomaly buffers in Space Engineers 1. Anomaly is a Pulsar client plugin. It does not change the picture by itself.

This wiki is the public copy of the in-editor shader developer canvas. When the public shader API changes, both should stay in sync.

## What Anomaly is

Anomaly intercepts Space Engineers’ DX11 shader compiler and publishes shared GPU textures. Other plugins bind those textures. Shader packs add or replace HLSL through the same compile door. You never Harmony-patch `MyShader`, `DrawGameScene`, atmosphere, or tonemap from a pack — ship `Fullscreen/<Slot>/*.hlsl` or register an owned-pass draw instead.

| | |
|--|--|
| **1** compile hook | Anomaly owns `MyShaderCompiler` |
| **Named** stages | Not 215 Keen files |
| **Bind** | Don’t draw Keen’s frame |

> **Who this is for.** Pack authors (HLSL overlay / inject), buffer consumers (SE-DLSS, TAA, SSR, motion blur), and anyone deciding whether Anomaly is the right place for a feature. Steam Workshop mods cannot ship packs.

### Buffer API

Anomaly writes velocity, linear depth, Hi-Z, previous-frame color, and a reactive mask. You resolve types by name at runtime. No compile-time reference to this repo.

→ [[Buffer-catalog|Catalog]] · [[Velocity-contract|Velocity]]

### Shader override API

A Pulsar plugin that depends on Anomaly drops `Overlay/`, `Inject/`, and `Fullscreen/` files. Overlay/Inject still draw through Keen. `Fullscreen/` is compiled and drawn by Anomaly.

→ [[Your-first-pack|First pack]] · [[Overlay-vs-inject|Overlay vs inject]] · [[Fullscreen-programs|Fullscreen programs]]

## What it is not

| Not this | Why |
|----------|-----|
| A graphics preset | It does not change the picture until a consumer or pack is enabled. |
| Iris / OptiFine for SE | SE already has a deferred engine. Anomaly rides Keen’s compiler; it does not replace the renderer. |
| ReShade | Present-time FX cannot see object velocity or GBuffer extras. |
| A second compile hook | Packs that Harmony-patch `MyShader` fight Anomaly and break Depth. |
| Workshop `.sbc` mods | Packs are Pulsar plugins with `DependencyIds` and hashed assets. |

## Install (to use the API)

Windows Space Engineers with [Pulsar](https://github.com/SpaceGT/Pulsar). Enable Anomaly Shader Framework (PluginHub or this repo’s local build). Any consumer or pack lists Anomaly as a dependency so Pulsar auto-enables it. Plugin id: `A9C29274-E447-49EE-881B-C980E6D190FD`. NVIDIA RTX is not required for Anomaly; SE-DLSS has its own GPU needs.

> **Warning — Rich HUD / SmoothFrames.** Unbind extra RT/SRV before returning to Keen. SmoothFrames also patches the render thread — do not assume you own `DrawGameScene`.

## Contents

**Getting started**

- [[Pick-a-path|Pick a path]]
- [[How-the-frame-works|How the frame works]]

**Ship HLSL**

- [[Your-first-pack|Your first pack]]
- [[Overlay-vs-inject|Overlay vs inject]]
- [[Named-stages|Named stages]]
- [[HLSL-cookbook|HLSL cookbook]]

**Consume buffers**

- [[Buffer-catalog|Buffer catalog]]
- [[Velocity-contract|Velocity contract]]
- [[Owned-passes|Owned passes]]
- [[Fullscreen-programs|Fullscreen programs]]
- [[Frame-graph|Frame graph]]

**Advanced**

- [[GBuffer-attachments|GBuffer attachments]]
- [[Pass-begin-binds|Pass-begin binds]]
- [[Composition-rules|Composition rules]]

**Reference**

- [[Troubleshooting]]
- [[Glossary]]
- [[Source-map|Source map]]
