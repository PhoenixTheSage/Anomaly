# GBuffer attachments

Anomaly owns extra GBuffer color targets. Request a slot. Do not splice `SV_Target3` — that is velocity.

| Slot | Owner |
|------|-------|
| SV_Target0–2 | Keen. Do not repack unless exclusive GBuffer. |
| GBuffer1.a | Unused Keen channel — cheap packed extra (id / flags). |
| SV_Target3 | Velocity. Packs cannot claim it. |
| SV_Target4+ | Next full attachments (object id, …). Max 8 color targets. |

## Request (C# or json)

```csharp
// ClientPlugin.Shaders.GBufferAttachments
Request("objectid", "R32_UINT");           // or RequestAttachment(...)
```

```json
"attachments": [{ "name": "objectid", "format": "R32_UINT" }]
```

Same name + same format from two packs share the slot. Mismatched formats fail closed. Depth permutations never see extra targets.

## What gets generated

Struct fields in `Anomaly/Extras/GBufferAttachmentFields.hlsli`. `AnomalyInitAttachments` zeros the whole `GbufferOutput` (FXC X3508 if any `SV_Target` field is left unwritten). Lighting samples `AnomalyAttach_objectid` at t6+ when `ANOMALY_ATTACH_OBJECTID` is defined. Packed extras use format `GBuffer1.a`.

`GbufferWrite` / `GbufferWriteBlend` keep Keen’s argument list unless `ANOMALY_VELOCITY` is set. Decals, foliage, and sprites include this header and must still compile.

→ [[Pass-begin-binds|How lighting actually sees the SRV]]
