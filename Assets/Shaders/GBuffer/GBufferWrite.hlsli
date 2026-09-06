#ifndef GBUFFER_WRITE_H__
#define GBUFFER_WRITE_H__

#include <Frame.hlsli>
#include <VertexTransformations.hlsli>

struct GbufferOutput 
{
    float4 gbuffer0 : SV_Target0;
    float4 gbuffer1 : SV_Target1;
    float4 gbuffer2 : SV_Target2;
#ifdef ANOMALY_VELOCITY
    float2 velocity : SV_Target3;
    // Reserved developer sideband. Target7 is bound only while the pipeline
    // audit view is active; otherwise D3D discards this output.
    float4 velocityAudit : SV_Target7;
#endif
#include <Anomaly/Extras/GBufferAttachmentFields.hlsli>
#ifdef CUSTOM_DEPTH
	float depth : SV_Depth;
#endif
};

#include <Anomaly/Extras/GBufferAttachmentInit.hlsli>

void GbufferWrite(out GbufferOutput output,
    float3 color, float metal, float gloss, float3 N, float ao, float emissive, uint coverage, uint lod
#ifdef ANOMALY_VELOCITY
    , float2 velocity
#endif
#ifdef CUSTOM_DEPTH
	, float depth
#endif
	)
{
    output = (GbufferOutput)0;
    AnomalyInitAttachments(output);
    float3 nview = normalize(world_to_view(N));
    float2 nenc = pack_normals2(nview);
    output.gbuffer0 = float4(color, lod / 255.f);
    output.gbuffer1 = float4(nenc, ao, 0);
    output.gbuffer2 = float4(metal, gloss, emissive, coverage / 255.f);

#ifdef ANOMALY_VELOCITY
    // Runtime final-output probe. This deliberately bypasses the VS -> PS
    // semantic and all velocity inputs without creating another cached shader
    // permutation. If Target3 is writable, MrtWrite must make every covered
    // GBuffer pixel positive-X (pink).
    output.velocity = AnomalyPixelProbe.z > 0.5 ? AnomalyPixelProbe.xy : velocity;
    // R: this PS invocation reached GbufferWrite. G: PS b7 probe enable.
    // BA: unmodified velocity argument arriving from the VS/PS semantic.
    output.velocityAudit = float4(1, AnomalyPixelProbe.z, velocity);
    // Known-good-lane proof for VelocityPipelineAudit. GBuffer0 is already a
    // proven Keen output, so magenta here distinguishes a missing b7 read from
    // Target3/Target7 output loss. MrtWrite is an invasive diagnostic by design.
    if (AnomalyPixelProbe.z > 0.5)
        output.gbuffer0.rgb = float3(1, 0, 1);
#endif

#ifdef CUSTOM_DEPTH
	output.depth = depth;
#endif
}

// Refer to MyMeshMaterial1.BindMaterialTextureBlendStates for blend state selection
void GbufferWriteBlend(out GbufferOutput output,
    float3 color, float metal, float3 normal, float gloss, float ao, float emissive, float alpha, float alphaN, float fadeAlpha
#ifdef ANOMALY_VELOCITY
    , float2 velocity
#endif
#ifdef CUSTOM_DEPTH
    , float depth
#endif
    )
{
    output = (GbufferOutput)0;
    AnomalyInitAttachments(output);
    output.gbuffer0 = float4(color, alpha) * fadeAlpha;

    // Don't multiply normals and ao because they are already multiplied by the blendstate
    float3 normalV = world_to_view(normal);
    float2 enc = pack_normals2(normalV);
    output.gbuffer1 = float4(enc, ao, alphaN * fadeAlpha);

    output.gbuffer2 = float4(metal, gloss, emissive, alpha) * fadeAlpha;

#ifdef ANOMALY_VELOCITY
    // Do not scale MVs by decal alpha; Target3 uses default (replace) blend.
    output.velocity = AnomalyPixelProbe.z > 0.5 ? AnomalyPixelProbe.xy : velocity;
    output.velocityAudit = float4(1, AnomalyPixelProbe.z, velocity);
    if (AnomalyPixelProbe.z > 0.5)
        output.gbuffer0.rgb = float3(1, 0, 1);
#endif

#ifdef CUSTOM_DEPTH
    output.depth = depth;
#endif
}

#endif
