#pragma pack_matrix(row_major)

// IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly with ContributeVelocity.
// Isolated.a is view-space hit distance in meters (0 = no overlay). Reconstruct
// camera MVs at that depth so DLSS can lock the curtain instead of rejecting
// history (Reactive) or using far-plane sky parallax.

cbuffer Constants : register(b0)
{
    float4x4 UnjitteredViewProj;
    float4x4 PrevViewProj;
    float4 CamToWorldR0;
    float4 CamToWorldR1;
    float4 CamToWorldR2;
    float2 RenderSize;
    float2 ProjScale;
    uint HistoryValid;
    uint Pad0;
    float2 Pad1;
};

Texture2D Isolated : register(t0);
Texture2D<float2> VelocityTex : register(t1);
SamplerState PointSampler : register(s0);

float2 SanitizeMv(float2 mv)
{
    if (!all(isfinite(mv)))
        return float2(0, 0);
    return clamp(mv, -RenderSize, RenderSize);
}

float3 ViewToWorld(float3 view)
{
    return CamToWorldR0.xyz * view.x
         + CamToWorldR1.xyz * view.y
         + CamToWorldR2.xyz * view.z;
}

float2 CameraVelocityAtDistance(float2 uv, float t)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float2 scale = max(ProjScale, 1e-6);
    float3 viewRay = float3(ndc.x / scale.x, ndc.y / scale.y, -1);
    float3 rayDir = normalize(ViewToWorld(viewRay));
    float3 world = rayDir * t;
    float4 currClip = mul(float4(world, 1), UnjitteredViewProj);
    currClip /= max(currClip.w, 1e-6);
    float4 prevClip = mul(float4(world, 1), PrevViewProj);
    prevClip /= max(prevClip.w, 1e-6);
    float2 currUv = float2(currClip.x * 0.5 + 0.5, 0.5 - currClip.y * 0.5);
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);
    return SanitizeMv((prevUv - currUv) * RenderSize);
}

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    uint2 pixel = uint2(pos.xy);
    float2 base = VelocityTex[pixel];
    float4 iso = Isolated.SampleLevel(PointSampler, uv, 0);
    if (iso.a <= 1e-3 || HistoryValid == 0)
        return float4(base, 0, 1);
    return float4(CameraVelocityAtDistance(uv, iso.a), 0, 1);
}
