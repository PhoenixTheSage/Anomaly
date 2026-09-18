#ifndef ANOMALY_VOLUME_SHADOWS_HLSLI
#define ANOMALY_VOLUME_SHADOWS_HLSLI
// Camera-relative matrices supplied by the owner of these maps. These are not
// Keen camera cascades and do not use the receiver's screen-space shadow mask.
Texture2D<float> AnomalyVolumeShadow0 : register(t20);
Texture2D<float> AnomalyVolumeShadow1 : register(t21);
Texture2D<float> AnomalyVolumeShadow2 : register(t22);
SamplerComparisonState AnomalyVolumeShadowSampler : register(s5);
cbuffer AnomalyVolumeShadowConstants : register(b8)
{
    row_major float4x4 AnomalyVolumeShadowMatrix[3];
    float4 AnomalyVolumeShadowRadii; // xyz radii; w current-frame validity
    float4 AnomalyVolumePlanet; // camera-relative center, opaque body radius
};

float AnomalyPlanetSunVisibility(float3 p, float3 sunToward)
{
    float3 relative = p - AnomalyVolumePlanet.xyz;
    float radius = AnomalyVolumePlanet.w;
    if (radius <= 0) return 1;
    float b = dot(relative, sunToward);
    float c = dot(relative, relative) - radius * radius;
    // The opaque sphere supplies the far planetary horizon. Terrain meshes
    // supply mountains/caves above it. Never use atmosphere radius here.
    return c < 0 || (b < 0 && b*b >= c) ? 0 : 1;
}

float AnomalySampleVolumeShadow(int cascade, float3 p)
{
    float4 projected = mul(float4(p,1), AnomalyVolumeShadowMatrix[cascade]);
    float3 q = projected.xyz / projected.w;
    float2 uv = q.xy * float2(.5,-.5) + .5;
    if (any(uv <= 0) || any(uv >= 1) || q.z < 0 || q.z > 1) return 0;
    // Bias is in metres, not normalized depth: a fixed 0.000015 on these
    // 32–48 km projections moves receivers by 0.5–0.7 m through thin walls.
    float radius = cascade==0?AnomalyVolumeShadowRadii.x:
                   cascade==1?AnomalyVolumeShadowRadii.y:AnomalyVolumeShadowRadii.z;
    float depthSpan = 32000 + 2*radius - 1; // owner near=1, far=reach+radius
    float z = q.z - .01 / depthSpan;
    if (cascade == 0) return AnomalyVolumeShadow0.SampleCmpLevelZero(AnomalyVolumeShadowSampler,uv,z);
    if (cascade == 1) return AnomalyVolumeShadow1.SampleCmpLevelZero(AnomalyVolumeShadowSampler,uv,z);
    return AnomalyVolumeShadow2.SampleCmpLevelZero(AnomalyVolumeShadowSampler,uv,z);
}

float AnomalyVolumeGeometryVisibility(float3 p, float3 sunToward)
{
    if (AnomalyVolumeShadowRadii.w < .5) return 0; // renderer also refuses activation
    float body = AnomalyPlanetSunVisibility(p,sunToward);
    if (body == 0) return 0;
    float radius = length(p);
    int cascade = radius < AnomalyVolumeShadowRadii.x*.85 ? 0 :
                  radius < AnomalyVolumeShadowRadii.y*.85 ? 1 : 2;
    float visibility = AnomalySampleVolumeShadow(cascade,p);
    if (cascade < 2)
    {
        float edge = cascade == 0 ? AnomalyVolumeShadowRadii.x : AnomalyVolumeShadowRadii.y;
        float blend = smoothstep(edge*.7,edge*.85,radius);
        float next = AnomalySampleVolumeShadow(cascade+1,p);
        // Do not fade a genuine high-resolution blocker to a coarser miss.
        visibility = lerp(visibility,min(visibility,next),blend);
    }
    return body*visibility;
}
#endif
