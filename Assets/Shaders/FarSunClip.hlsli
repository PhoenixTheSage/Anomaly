#ifndef ANOMALY_FAR_SUN_CLIP
#define ANOMALY_FAR_SUN_CLIP
cbuffer AnomalyFarSunClip : register(b13) {
    row_major float4x4 FarSunInverseProjection;
    float4 FarSunClipParameters; // map dimension, resident radius, reserved
};
void AnomalyClipResident(float4 position) {
    float2 uv = position.xy / FarSunClipParameters.x;
    float4 world = mul(float4(uv.x * 2 - 1, 1 - uv.y * 2, position.z, 1), FarSunInverseProjection);
    clip(length(world.xyz / world.w) - FarSunClipParameters.y);
}
#endif
