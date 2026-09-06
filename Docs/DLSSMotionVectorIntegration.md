# DLSS motion-vector integration handoff

Inspected September 6, 2026 against the local `T:/Cursor Projects/SE-DLSS` checkout. This is a proposed consumer update, not an implemented DLSS change or proof of production-ready Anomaly vectors.

## Existing integration

`ClientPlugin/Dlss/AnomalyHook.cs` already discovers `VelocityRegistry.Active` without a compile-time dependency and reads availability, native resource, size, convention, and history validity. `DlssRuntime.cs` selects it before evaluation, retains a camera fallback, and resets on source changes, cuts, or invalid history. `NgxApi.cs` binds the supplied resource. Keep these paths; no second object-history producer is needed in DLSS.

## Required changes and checks

1. **Direction negotiation.** Anomaly now emits current-to-previous pixel displacement, Y down, with `CurrentToPrevious = 8` (combined flags 15). Keep NGX scale (+1,+1) for this revision. Legacy Anomaly flags 7 emitted the opposite direction and require (-1,-1). The existing fixed positive NGX scale matches the new producer, but the consumer should explicitly validate direction and handle or reject legacy producers. Inspect the camera fallback convention independently. Do not apply Streamline normalized-coordinate scaling to this direct NGX path. A point moving 3 pixels right must supply -3 pixels to NGX without double inversion.
2. **Reject incompatible data.** `AnomalyHook.TryGetLive` currently only logs missing convention flags and still returns the resource. Return fallback instead. Preserve the existing availability/nonzero-pointer/dimension checks; check actual texture format/device compatibility at binding. Read the live resource each evaluate; Anomaly retains ownership, and consumers must not dispose or cache a raw pointer across resize/device reset.
3. **Preserve flags and timing.** Internal-resolution unjittered motion requires MVLowRes on and MVJittered off; send jitter separately. Evaluate after Anomaly's scheduler-completion publication and against matching color/depth/internal dimensions. Verify actual configured flags rather than only comments. Keep history/source/cut resets and AfterUpscale notification.
4. **Reject probe frames.** Debug persistence and gain only affect a private visualization, but runtime velocity probes deliberately alter the producer. Reject probe-active frames or disable probes for DLSS testing, with an explicit fallback reason. The public velocity interface currently does not encode probe validity or a producer frame ID: a future optional capability can make this validation robust without breaking existing readers.
5. **Add binding proof.** Log selected source, frame, native resource identity, format, dimensions, applied XY scale, history/reset reason, and actual NGX motion resource. These must describe the evaluated input, not merely an available registry object.

## Acceptance

Test both sources with fixed-camera moving grids, camera translation/rotation over static voxels, characters, cuts, DRS/resize, and source toggles. Compare previous-frame reprojection and ghosting with identical settings. Numeric scale/sign and consecutive-frame alignment must pass independently of debug colors or persistence. Motion-responsive output is confirmed; correctness and full coverage remain open.

NVIDIA reference: [DLSS Programming Guide](https://github.com/NVIDIA/DLSS/blob/main/doc/DLSS_Programming_Guide_Release.pdf), motion-vector requirements (current position plus motion locates the previous position). Local code references above are relative to the SE-DLSS repository.
