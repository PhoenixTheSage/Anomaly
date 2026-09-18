Texture2D<float4> VolumeMotionCoverage:register(t0);
float4 __pixel_shader(float4 pos:SV_Position,float2 uv:TEXCOORD0):SV_Target
{
    float4 value=VolumeMotionCoverage.Load(int3((int2)pos.xy,0));
#if VOLUME_REACTIVE
    // Conservative rejection for mixed fog/surface pixels with no single velocity.
    return max(value.z,min(value.w,1-value.w)*2);
#else
    return float4(value.xy,0,value.w);
#endif
}
