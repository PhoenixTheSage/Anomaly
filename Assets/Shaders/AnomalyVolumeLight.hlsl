RWTexture3D<float> LightOpticalDepth : register(u0);
[numthreads(8,8,1)] void __compute_shader(uint3 id:SV_DispatchThreadID)
{
    uint size=(uint)VolumeControl.z,slices=(uint)VolumeControl.w;
    if(any(id.xy>=size)) return;
    float tau=0,stepMeters=VolumeSun.w/slices;
    for(uint z=0;z<slices;z++)
    {
        float3 q=float3(((id.xy+.5)/size)*float2(2,-2)+float2(-1,1),(z+.5)/slices);
        float3 p=mul(float4(q,1),VolumeLightToCamera).xyz;
        float delta=VolumeExtinction(p)*stepMeters;
        LightOpticalDepth[uint3(id.xy,z)]=min(tau+delta*.5,80);
        tau=min(tau+delta,80);
    }
}
