#ifndef ANOMALY_CELESTIAL_HLSLI
#define ANOMALY_CELESTIAL_HLSLI
#include <Frame.hlsli>
#include <VertexTransformations.hlsli>
#include <PixelUtils.hlsli>

Texture2D<float> AnomalyCelestialDepth : register(t0);
StructuredBuffer<float4> AnomalyCelestialData : register(t1);
// Optional pack art (Texture2DArray). Missing/unbound samples as transparent black.
Texture2DArray<float4> AnomalyCelestialArt : register(t2);
SamplerState AnomalyCelestialArtSampler : register(s0);
cbuffer AnomalyCelestialView : register(b6)
{
    float4 CelestialViewR0, CelestialViewR1, CelestialViewR2;
    float4 CelestialView; // target width/height, probe projection x/y scale
    float4 CelestialDataInfo; // float4 record count
};
cbuffer AnomalyCelestialUniforms : register(b7) { float4 CelestialUniform[16]; };

struct AnomalyCelestialInput
{
    float3 direction; // normalized world direction away from camera; never position
    float3 directionDx, directionDy; // evaluated before depth discard; pixel integration basis
    float3 sunDirection;
    float3 sunRadiance; // Keen disc color * directional color * disc intensity
    float angularPixel;
    uint isProbe;
    uint dataCount;
};

float AnomalyCelestialFootprint(float3 ray)
{
    return max(max(length(ddx(ray)), length(ddy(ray))), 1e-8);
}

// Stable chord distance avoids acos(dot) precision loss at narrow FOV.
float AnomalyCelestialDisc(float3 ray, float3 center, float radius, float pixelWidth)
{
    float chord = length(ray - center);
    float edge = 2 * sin(radius * 0.5);
    return 1 - smoothstep(max(0, edge - pixelWidth * 0.5), edge + pixelWidth * 0.5, chord);
}
#endif
