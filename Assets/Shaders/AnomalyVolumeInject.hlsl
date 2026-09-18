RWTexture3D<float4> Coefficients : register(u0);
RWTexture3D<float4> Source : register(u1);
RWTexture3D<float4> Motion : register(u2);
RWTexture3D<float> Visibility : register(u3);
RWTexture2D<float> RepresentativeDepth : register(u4);
[numthreads(4,4,4)] void __compute_shader(uint3 id:SV_DispatchThreadID)
{
    if(any(id>=(uint3)VolumeGrid.xyz)) return;
    if(id.z==0) RepresentativeDepth[id.xy]=VolumeDepth((id.xy+.5)/VolumeGrid.xy);
    float3 p=VolumePosition(id),ray=normalize(p);
    float sigma,visibility; float3 source,velocity;
    VolumeEvaluate(p,ray,sigma,source,velocity,visibility);
    float reactive=1;
    if(VolumeTemporal.y>.5)
    {
        float3 previousPosition=p+VolumeCameraDelta.xyz-velocity*VolumeTemporal.x;
        float2 uv;
        bool validProjection=VolumePreviousUv(previousPosition,uv);
        float depth=length(previousPosition);
        float slice=VolumeSlice(depth)/VolumeGrid.z;
        if(validProjection && slice>0 && slice<1)
        {
            float3 coord=float3(uv,slice);
            float4 oldCoefficients=VolumePreviousCoefficients.SampleLevel(VolumeLinearClamp,coord,0);
            float4 oldSource=VolumePreviousSource.SampleLevel(VolumeLinearClamp,coord,0);
            float oldDepth=VolumePreviousDepth.SampleLevel(AnomalyPointSampler,uv,0);
            float densityError=abs(oldCoefficients.a-sigma)/max(sigma,.00001);
            float lightError=abs(oldSource.a-visibility);
            bool accept=densityError<.15 && lightError<.1 && depth<=oldDepth+max(1,oldDepth*.01);
            if(accept) { source=lerp(source,clamp(oldSource.rgb,source*.65,source*1.35),.75); reactive=saturate(densityError*5+lightError*5); }
        }
    }
    Coefficients[id]=float4(0,0,0,min(sigma,65504));
    Source[id]=float4(min(max(source,0),65504),visibility);
    Motion[id]=float4(velocity,reactive);
    Visibility[id]=visibility;
}
