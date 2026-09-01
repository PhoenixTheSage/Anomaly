#ifndef SURFACE_H__
#define SURFACE_H__

// Anomaly wrap of Keen GBuffer/Surface.hlsli. Overlay requires exclusive
// ["GBuffer"] or ["Lighting"]. Do not add fields unless read_gbuffer fills them;
// extra attachments are SRVs in GBuffer.hlsli, not SurfaceInterface members.

struct SurfaceInterface 
{
	float3 	base_color;
    uint    LOD;
	float  	metalness;
	float3  albedo;
	float3 	f0;
	float 	gloss;
	float 	ao;
	float 	ao_original;
	float 	emissive;

	float3 	position;
	float3 	positionView;
	float3 	N;
	float3 	NView;
	float3 	V;
	float3  VSkybox;
	float3 	VView;

	float 	native_depth;
	float 	depth;
	uint 	coverage;
	uint 	stencil;
};

#endif
