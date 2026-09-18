struct VolumePixelOutput
{
    float4 color:SV_Target0; // radiance, transmittance; one final HDR composite
    float4 motion:SV_Target1; // render-pixel motion, reactive coverage, opacity
};
float4 VolumePrefix(float2 uv,float distance)
{
    float boundary=VolumeSlice(distance);
    if(boundary<=0) return float4(0,0,0,1);
    float z=(boundary-.5)/VolumeGrid.z;
    float4 value=VolumeIntegrated.SampleLevel(VolumeLinearClamp,float3(uv,max(z,.5/VolumeGrid.z)),0);
    if(boundary<1) { value.rgb*=boundary; value.a=pow(max(value.a,1e-8),boundary); }
    return value;
}
VolumePixelOutput __pixel_shader(float4 pos:SV_Position,float2 uv:TEXCOORD0)
{
    VolumePixelOutput o;
    float distance=VolumeDepth(uv);
    float3 ray=VolumeRay(uv);
    float boundary=floor(VolumeSlice(distance));
    float start=AnomalyVolumeSliceDistance((uint)boundary,(uint)VolumeGrid.z,1,VolumeGrid.w);
    // Exact sampling close to the camera prevents room/silhouette interpolation
    // from pulling outdoor haze across a nearby cockpit wall.
    bool near=distance<64;
    if(near) start=0;
    float4 volume=float4(0,0,0,1);
    if(start>0)
    {
        float2 cell=uv*VolumeGrid.xy-.5;
        int2 base=(int2)floor(cell); float2 fraction=frac(cell);
        float weightSum=0; volume=0;
        [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++)
        {
            int2 q=clamp(base+int2(x,y),0,(int2)VolumeGrid.xy-1);
            float2 sampleUv=(q+.5)/VolumeGrid.xy;
            float sampleDepth=VolumePreviousDepth.Load(int3(q,0));
            float spatial=(x?fraction.x:1-fraction.x)*(y?fraction.y:1-fraction.y);
            float difference=abs(sampleDepth-distance)/max(1,distance*.02);
            float weight=spatial/(1+difference*difference);
            volume+=VolumePrefix(sampleUv,start)*weight;
            weightSum+=weight;
        }
        volume/=max(weightSum,1e-10);
    }
    uint refinements=near?8:2;
    float step=(distance-start)/refinements;
    float sigma=0,visibility=0; float3 source=0,velocity=0;
    [loop] for(uint i=0;i<refinements;i++)
    {
        float3 p=ray*(start+(i+.5)*step);
        VolumeEvaluate(p,ray,sigma,source,velocity,visibility);
        AnomalyIntegrateMedium(sigma,source,step,volume.rgb,volume.a);
    }
    float representative=max(distance*.5,1);
    float3 coord=float3(uv,saturate(VolumeSlice(representative)/VolumeGrid.z));
    float4 flow=VolumeVelocity.SampleLevel(VolumeLinearClamp,coord,0);
    float3 representativePosition=ray*representative;
    float2 previousUv;
    bool validMotion=VolumePreviousUv(representativePosition+VolumeCameraDelta.xyz-flow.xyz*VolumeTemporal.x,previousUv);
    float2 motion=validMotion?(previousUv-uv)*VolumeScreen.xy:0;
    float coverage=saturate(1-volume.a);
    float reactive=max(flow.w,VolumeTemporal.y<.5 || !validMotion?1:0)*coverage;
    o.color=float4(min(max(volume.rgb,0),65504),saturate(volume.a));
    o.motion=float4(motion,saturate(reactive),coverage);
    if(VolumeTemporal.w>.5)
    {
        int debug=(int)VolumeTemporal.w;
        float3 p=ray*max(distance*.5,1);
        float v=0;
        if(debug==1) v=saturate(VolumeExtinction(p)*1000);
        if(debug==2) v=AnomalyVolumeGeometryVisibility(p,VolumeSun.xyz);
        if(debug==3) v=VolumeSunTransmittance(p);
        if(debug==4) v=AnomalyInsideSealedRoom(p,(uint)VolumeControl.y)?1:0;
        if(debug==5) { float d=length(p); o.color=float4(d<128?float3(1,0,0):d<1000?float3(0,1,0):float3(0,0,1),0); return o; }
        if(debug==6) v=flow.w;
        o.color=float4(v.xxx,0);
    }
    return o;
}
