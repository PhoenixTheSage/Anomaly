# GBuffer attachments

Anomaly owns extra GBuffer color targets. Request a slot. Do not splice `SV_Target3` — that is velocity.

| Slot | Owner |
|------|-------|
| SV_Target0–2 | Keen. Do not repack unless exclusive GBuffer. |
| GBuffer1.a | Unused Keen channel — cheap packed extra (id / flags). |
| SV_Target3 | Velocity. Packs cannot claim it. |
| SV_Target4–6 | Pack-owned full attachments (object id, …). |
| SV_Target7 | Reserved internal sideband for `VelocityPipelineAudit`. |

## Request (C# or json)

```csharp
// ClientPlugin.Shaders.GBufferAttachments
Request("objectid", "R32_UINT");           // or RequestAttachment(...)
```

```json
"attachments": [{ "name": "objectid", "format": "R32_UINT" }]
```

Same name + same format from two packs share the slot. Mismatched formats fail closed. Depth permutations never see extra targets.

On Vulkan the stock pass stays three color targets plus depth. `GBuffer/<name>.hlsl` (or `"file"`) is the write shader: entry `__pixel_shader`, `SV_Target0` out, no descriptors and no push constants. The vertex shader passes `SV_Position` and `nointerpolation` object rows `row0`–`row2` on `TEXCOORD0`–`TEXCOORD2` (the same `object_.matrix_row*` layout). A shader that only declares `SV_Position` still links. The return type matches the format (`uint` for `R32_UINT`, `float2` for `R16G16_FLOAT`, `float4` for `R8G8B8A8_UNORM` and `R16G16B16A16_FLOAT`). `GBufferTargetRegistry.Request` is the C# form in `Anomaly.dll`. The catalog name is the attachment name. Sentinel `0xFFFF000000000009` is slot 0 (`SV_Target4`). `RequestSrv` samples it. Skinned characters, voxels, foliage, and decals stay clear on that replay path. Owner GBuffer generations on hosts reporting native owner flag8 also write the fields and packed `GBuffer1.a` from the owning invocation, exactly as on DX11; the write shader is then optional.

## What gets generated

Struct fields in `Anomaly/Extras/GBufferAttachmentFields.hlsli`. `AnomalyInitAttachments` zeros the whole `GbufferOutput` (FXC X3508 if any `SV_Target` field is left unwritten). Lighting samples `AnomalyAttach_objectid` at t6+ when `ANOMALY_ATTACH_OBJECTID` is defined. Packed extras use format `GBuffer1.a`.

`GbufferWrite` / `GbufferWriteBlend` keep Keen’s argument list unless `ANOMALY_VELOCITY` is set. Decals, foliage, and sprites include this header and must still compile.

→ [[Pass-begin-binds|How lighting actually sees the SRV]]
