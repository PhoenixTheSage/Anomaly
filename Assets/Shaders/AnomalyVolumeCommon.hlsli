#ifndef ANOMALY_VOLUME_COMMON
#define ANOMALY_VOLUME_COMMON
#include "AnomalyMedium.hlsli"
#include "AnomalyVolumeShadows.hlsli"
#include "AnomalyVolumeInteriors.hlsli"
cbuffer VolumeFrame : register(b6)
{
    row_major float4x4 VolumeCameraToWorld;
    row_major float4x4 VolumeCurrentProjection;
    row_major float4x4 VolumePreviousProjection;
    row_major float4x4 VolumeLightToCamera;
    float4 VolumeGrid; // xyz dimensions, w far metres
    float4 VolumeScreen; // xy dimensions, zw projection scales
    float4 VolumeSun; // direction toward sun, light-volume depth in metres
    float4 VolumeSunEnergy;
    float4 VolumeAmbient;
    float4 VolumeTemporal; // dt, history valid, frame, debug view
    float4 VolumeCameraDelta;
    float4 VolumeControl; // provider count, interior count, light resolution, light slices
};
cbuffer VolumeProviders : register(b7)
{
    float4 VolumeUniforms[4][16];
    float4 VolumeOrigins[4];
    float4 VolumeBoundsMin[4];
    float4 VolumeBoundsMax[4];
    float4 VolumeProviderMotion[4];
};
SamplerState VolumeLinearClamp : register(s0);
SamplerState AnomalyWrapSampler : register(s1);
SamplerState AnomalyPointSampler : register(s2);
Texture2D<float> VolumeSceneDepth : register(t0);
Texture3D<float> VolumeLightTau : register(t1);
Texture3D<float4> VolumePreviousSource : register(t2);
Texture3D<float4> VolumePreviousCoefficients : register(t3);
Texture3D<float4> VolumeIntegrated : register(t4);
Texture3D<float4> VolumeCurrentSource : register(t5);
Texture3D<float4> VolumeCurrentCoefficients : register(t6);
Texture3D<float4> VolumeVelocity : register(t7);
Texture3D<float> VolumeVisibility : register(t8);
Texture2D<float> VolumePreviousDepth : register(t9);

float3 VolumeRay(float2 uv)
{
    float3 view=float3((uv*float2(2,-2)+float2(-1,1))/VolumeScreen.zw,-1);
    return normalize(mul(float4(view,0),VolumeCameraToWorld).xyz);
}
float VolumeDepth(float2 uv)
{
    uint w,h; VolumeSceneDepth.GetDimensions(w,h);
    float raw=VolumeSceneDepth.Load(int3(min(uint2(uv*float2(w,h)),uint2(w-1,h-1)),0));
    // Reversed-Z infinite perspective: uploaded projection M43/M33.
    float viewZ=VolumeSunEnergy.w / max(raw+VolumeAmbient.w,1e-9);
    float3 view=float3((uv*float2(2,-2)+float2(-1,1))/VolumeScreen.zw,-1);
    return raw<=0?VolumeGrid.w:min(length(view)*abs(viewZ),VolumeGrid.w);
}
// Projection behind the previous camera or outside its viewport has no usable
// history. Never encode the division by near-zero w into a float16 motion target.
bool VolumePreviousUv(float3 previousPosition,out float2 uv)
{
    float4 previous=mul(float4(previousPosition,1),VolumePreviousProjection);
    uv=0;
    if(any(!isfinite(previous)) || previous.w<=1e-5) return false;
    uv=previous.xy/previous.w*float2(.5,-.5)+.5;
    return all(isfinite(uv)) && all(uv>0) && all(uv<1);
}
float VolumeSlice(float distance)
{ return log2(1+max(distance,0))/log2(1+VolumeGrid.w)*VolumeGrid.z; }
float3 VolumePosition(uint3 cell)
{
    float a=AnomalyVolumeSliceDistance(cell.z, (uint)VolumeGrid.z,1,VolumeGrid.w);
    float b=AnomalyVolumeSliceDistance(cell.z+1,(uint)VolumeGrid.z,1,VolumeGrid.w);
    return VolumeRay((cell.xy+.5)/VolumeGrid.xy)*((a+b)*.5);
}
float VolumeSunTransmittance(float3 p)
{
    float4 q=mul(float4(p,1),AnomalyVolumeShadowMatrix[2]);
    float3 uvw=float3(q.xy*float2(.5,-.5)+.5,q.z);
    if(any(uvw<0)||any(uvw>1)) return 0; // coverage is a prerequisite, never a stale shadow
    return exp(-max(VolumeLightTau.SampleLevel(VolumeLinearClamp,uvw,0),0));
}
#endif
