# Changelog

Anomaly uses semantic versioning. The project began at `1.0.0`; the lineage below is reconstructed from repository history so the current version reflects shipped capability rather than the inherited SE-DLSS version.

## 1.5.0 — 2026-09-06

- Publishes visually proven object and camera motion vectors in current-to-previous pixel-space convention.
- Adds persistent motion visualization and a dedicated Velocity Debug page.
- Separates concise framework and velocity status screens.
- Keeps probes, checkpoint copies, and the full-resolution pipeline-audit target opt-in.
- Documents the consumer contract and DLSS integration requirements.

## Semantic lineage

- `1.0` — Pulsar shader-framework foundation and initial pack model.
- `1.1` — compile interception, camera velocity, ActorID history, and GBuffer object velocity.
- `1.2` — programmatic pack registration, rollback validation, and named overlays.
- `1.3` — additive injection, pack defines, GBuffer attachments, lighting binds, named buffers, and expanded stages.
- `1.4` — owned render slots, data-driven fullscreen programs, depth/history catalogs, and temporal participation.
- `1.5` — corrected and visually proven motion-vector output, audit instrumentation, persistence, compatibility guidance, and production-safe debug gating.

The intermediate entries describe compatibility epochs, not previously tagged releases.
