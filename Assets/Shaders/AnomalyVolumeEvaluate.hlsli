float VolumeExtinction(float3 p)
{
    if(AnomalyInsideSealedRoom(p,(uint)VolumeControl.y)) return 0;
    float sigma=0;
    [loop] for(uint i=0;i<(uint)VolumeControl.x;i++) sigma+=max(VolumeGetMedium(i,p).extinction,0);
    return sigma;
}
void VolumeEvaluate(float3 p,float3 viewRay,out float sigma,out float3 source,out float3 velocity,out float visibility)
{
    sigma=0; source=0; velocity=0; visibility=0;
    if(AnomalyInsideSealedRoom(p,(uint)VolumeControl.y)) return;
    float geometry=AnomalyVolumeGeometryVisibility(p,VolumeSun.xyz);
    visibility=geometry*VolumeSunTransmittance(p);
    [loop] for(uint i=0;i<(uint)VolumeControl.x;i++)
    {
        AnomalyMediumSample m=VolumeGetMedium(i,p);
        AnomalyAccumulateMedium(m,dot(viewRay,VolumeSun.xyz),visibility,VolumeSunEnergy.rgb,
            VolumeAmbient.rgb,sigma,source,velocity);
    }
    velocity=sigma>1e-8?velocity/sigma:0;
}
