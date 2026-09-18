#include "AnomalyMedium.hlsli"
// Coefficients.rgb = scattering coefficient; .a = extinction (/metre).
// Source.rgb already contains emission + shadowed single scattering (/metre).
Texture3D<float4> VolumeCoefficients : register(t0);
Texture3D<float4> VolumeSource : register(t1);
RWTexture3D<float4> IntegratedVolume : register(u0);
cbuffer Integration : register(b0)
{
    uint3 VolumeSize;
    float VolumeDistance;
    float VolumeNear;
    float3 IntegrationPadding;
};

[numthreads(8,8,1)]
void __compute_shader(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= VolumeSize.xy)) return;
    float3 radiance=0;
    float transmittance=1;
    float previous=0;
    for(uint z=0;z<VolumeSize.z;z++)
    {
        float end=AnomalyVolumeSliceDistance(z+1,VolumeSize.z,VolumeNear,VolumeDistance);
        float sigma=VolumeCoefficients.Load(int4(id.xy,z,0)).a;
        float3 source=VolumeSource.Load(int4(id.xy,z,0)).rgb;
        // Failed provider outputs must not poison the HDR history indefinitely.
        if (!isfinite(sigma) || any(!isfinite(source))) { sigma=0; source=0; }
        AnomalyIntegrateMedium(sigma,source,end-previous,radiance,transmittance);
        IntegratedVolume[uint3(id.xy,z)]=float4(min(radiance,65504),saturate(transmittance));
        previous=end;
    }
}
