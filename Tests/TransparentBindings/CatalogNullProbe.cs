using System;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.Mathematics.Interop;
using Buffer=SharpDX.Direct3D11.Buffer;
using Device=SharpDX.Direct3D11.Device;

static class CatalogNullProbe
{
    static RawVector4 Run(Device device,string declarations,string expression)
    {
        string source=declarations+"\nRWStructuredBuffer<float4> Output:register(u0);[numthreads(1,1,1)]void main(){"+expression+"}";
        using var code=ShaderBytecode.Compile(source,"main","cs_5_0",ShaderFlags.OptimizationLevel3);
        using var shader=new ComputeShader(device,code);
        using var output=new Buffer(device,new BufferDescription{SizeInBytes=16,Usage=ResourceUsage.Default,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,StructureByteStride=16});
        using var view=new UnorderedAccessView(device,output);
        using var staging=new Buffer(device,new BufferDescription{SizeInBytes=16,Usage=ResourceUsage.Staging,CpuAccessFlags=CpuAccessFlags.Read});
        var context=device.ImmediateContext;context.ComputeShader.Set(shader);context.ComputeShader.SetUnorderedAccessView(0,view);context.Dispatch(1,1,1);context.ComputeShader.SetUnorderedAccessView(0,null);
        context.CopyResource(output,staging);DataStream data;context.MapSubresource(staging,MapMode.Read,SharpDX.Direct3D11.MapFlags.None,out data);
        try{return data.Read<RawVector4>();}finally{context.UnmapSubresource(staging,0);data.Dispose();}
    }
    static float Decode(uint value,int mantissa)
    {
        int exponent=(int)(value>>mantissa);uint fraction=value&((1u<<mantissa)-1);
        return (float)(exponent==0?fraction*Math.Pow(2,1-15-mantissa):(1+fraction/Math.Pow(2,mantissa))*Math.Pow(2,exponent-15));
    }
    static void Packed(Device device,string name)
    {
        const string shader="float4 VS(uint id:SV_VertexID):SV_Position{float2 p=float2((id<<1)&2,id&2);return float4(p*float2(2,-2)+float2(-1,1),0,1);}float4 PS():SV_Target{return float4(128.0/255,1,.64,1);}";
        using var vsCode=ShaderBytecode.Compile(shader,"VS","vs_5_0");using var psCode=ShaderBytecode.Compile(shader,"PS","ps_5_0");
        using var vs=new VertexShader(device,vsCode);using var ps=new PixelShader(device,psCode);
        var desc=new Texture2DDescription{Width=1,Height=1,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R11G11B10_Float,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget};
        using var target=new Texture2D(device,desc);using var rtv=new RenderTargetView(device,target);
        desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;using var staging=new Texture2D(device,desc);
        var blend=new BlendStateDescription();blend.RenderTarget[0].IsBlendEnabled=true;blend.RenderTarget[0].SourceBlend=BlendOption.One;blend.RenderTarget[0].DestinationBlend=BlendOption.One;blend.RenderTarget[0].BlendOperation=BlendOperation.Add;blend.RenderTarget[0].SourceAlphaBlend=BlendOption.One;blend.RenderTarget[0].DestinationAlphaBlend=BlendOption.Zero;blend.RenderTarget[0].AlphaBlendOperation=BlendOperation.Add;blend.RenderTarget[0].RenderTargetWriteMask=ColorWriteMaskFlags.All;
        using var state=new BlendState(device,blend);var context=device.ImmediateContext;context.ClearRenderTargetView(rtv,new RawColor4(0,0,0,0));context.OutputMerger.SetTargets(rtv);context.OutputMerger.SetBlendState(state);
        context.Rasterizer.SetViewport(0,0,1,1);context.InputAssembler.PrimitiveTopology=SharpDX.Direct3D.PrimitiveTopology.TriangleList;context.VertexShader.Set(vs);context.PixelShader.Set(ps);context.Draw(3,0);context.OutputMerger.SetTargets((RenderTargetView)null);
        context.CopyResource(target,staging);DataStream data;context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None,out data);
        try{uint bits=data.Read<uint>();float red=Decode(bits&2047,6),green=Decode((bits>>11)&2047,6),blue=Decode(bits>>22,5);Console.WriteLine("DX11 "+name+" packed RGB="+red+","+green+","+blue);if(red!=.5f||green!=1||blue!=.625f)throw new Exception("DX11 packed HDR control changed");}
        finally{context.UnmapSubresource(staging,0);data.Dispose();}
    }
    internal static void Run(Device device)
    {
        Packed(device,"WARP");using(var hardware=new Device(SharpDX.Direct3D.DriverType.Hardware,DeviceCreationFlags.None))Packed(hardware,"HAL");
        foreach(var probe in new[]{
            new[]{"structured","StructuredBuffer<float4> Data:register(t7);","uint count,stride;Data.GetDimensions(count,stride);Output[0]=float4(count,stride,Data[0].x,Data[7].y);"},
            new[]{"2d","Texture2D<float4> Image:register(t6);","uint w,h;Image.GetDimensions(w,h);Output[0]=float4(w,h,Image.Load(int3(0,0,0)).ra);"},
            new[]{"3d","Texture3D<float4> Image:register(t6);","uint w,h,d;Image.GetDimensions(w,h,d);Output[0]=float4(w,h,d,Image.Load(int4(0,0,0,0)).r);"},
            new[]{"array","Texture2DArray<float4> Image:register(t6);","uint w,h,d;Image.GetDimensions(w,h,d);Output[0]=float4(w,h,d,Image.Load(int4(0,0,0,0)).r);"},
            new[]{"typed","Buffer<float4> Data:register(t7);","uint count;Data.GetDimensions(count);Output[0]=float4(count,Data[0].xyz);"}})
        {
            var value=Run(device,probe[1],probe[2]);Console.WriteLine("DX11 null "+probe[0]+"="+value.X+","+value.Y+","+value.Z+","+value.W);
            if(value.X!=0||value.Z!=0||value.W!=0||probe[0]!="structured"&&value.Y!=0)throw new Exception("DX11 null resource contract changed");
        }
    }
}
