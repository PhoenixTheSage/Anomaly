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
    output.velocity = velocity;
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
    output.velocity = velocity;
#endif

#ifdef CUSTOM_DEPTH
    output.depth = depth;
#endif
}

#endif
