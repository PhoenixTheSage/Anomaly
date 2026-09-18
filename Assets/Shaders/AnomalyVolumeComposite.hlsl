Texture2D<float4> VolumeColor:register(t0);
float4 __pixel_shader(float4 pos:SV_Position,float2 uv:TEXCOORD0):SV_Target
{ return VolumeColor.Load(int3((int2)pos.xy,0)); }
