using System;
using ClientPlugin.ShaderFramework;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

// Actual installed Billboards.hlsl PS, with a synthetic atlas/billboard and
// fullscreen VS. Exercises inherited b0/LinearSampler and native soft depth.
static class NativeBillboardProbe
{
    const string Vertex = @"
struct Out { float4 p : SV_Position; float2 uv : TEXCOORD0; uint i : TEXCOORD1; float3 w : TEXCOORD2; };
Out VS(uint id : SV_VertexID) {
 float2 p = float2((id << 1) & 2, id & 2); Out o;
 o.p=float4(p*float2(2,-2)+float2(-1,1),.5,1); o.uv=float2(1.25,.5); o.i=0; o.w=0; return o;
}";
    internal static void Run(Device device, byte[] pixelCode, bool deferred, Action<bool,string> check)
    {
        using var context = deferred ? new DeviceContext(device) : null;
        var native = context ?? device.ImmediateContext;
        native.ClearState();
        var rc = new MyRenderContext(native);
        using var reflection = new ShaderReflection(pixelCode);
        var variable = reflection.GetVariable("frame_");
        var frameValues = new float[(variable.Description.StartOffset + variable.Description.Size + 15) / 16 * 4];
        var type = variable.GetVariableType(); int offset = variable.Description.StartOffset;
        foreach (var part in new[] { "Environment", "projection_matrix" }) { type=type.GetMemberType(part); offset+=type.Description.Offset; }
        frameValues[offset/4+(type.Description.Class == ShaderVariableClass.MatrixColumns ? 11 : 14)]=.1f; // complementary depth
        using var frameData = new DataStream(frameValues.Length*4,true,true);
        frameData.WriteRange(frameValues); frameData.Position=0;
        using var frame = new Buffer(device,frameData,new BufferDescription(frameValues.Length*4,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0));
        MyCommon.FrameConstants=frame;
        var sd=new SamplerStateDescription { Filter=Filter.MinMagMipLinear, AddressU=TextureAddressMode.Wrap, AddressV=TextureAddressMode.Wrap, AddressW=TextureAddressMode.Wrap, MaximumLod=float.MaxValue };
        using var sampler=new SamplerState(device,sd);
        MySamplerStateManager.StandardSamplers=new[] { sampler,sampler,sampler }; MySamplerStateManager.Shadowmap=sampler;
        using var data=new DataStream(48,true,true);
        data.WriteRange(new float[] { .25f,.5f,.75f,1, 0,0,1,0, 0,0,0,1 }); data.Position=0;
        using var billboard=new Buffer(device,data,new BufferDescription(48,ResourceUsage.Immutable,BindFlags.ShaderResource,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,48));
        using var billboardSrv=new ShaderResourceView(device,billboard);
        var td=new Texture2DDescription { Width=2,Height=1,MipLevels=1,ArraySize=1,Format=Format.R32G32B32A32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource };
        using var atlasData=new DataStream(32,true,true); atlasData.Write(new RawVector4(1,1,1,1)); atlasData.Write(new RawVector4(0,0,0,0)); atlasData.Position=0;
        using var atlas=new Texture2D(device,td,new DataRectangle(atlasData.DataPointer,32)); using var atlasSrv=new ShaderResourceView(device,atlas);
        td.Width=1; td.Format=Format.R32_Float;
        using var depthData=new DataStream(4,true,true); depthData.Write(.01f); depthData.Position=0;
        using var depth=new Texture2D(device,td,new DataRectangle(depthData.DataPointer,4)); using var depthSrv=new ShaderResourceView(device,depth);
        depthData.Position=0; depthData.Write(.75f); depthData.Position=0;
        using var front=new Texture2D(device,td,new DataRectangle(depthData.DataPointer,4)); using var frontSrv=new ShaderResourceView(device,front);
        td.Format=Format.R32G32B32A32_Float; td.Usage=ResourceUsage.Default; td.BindFlags=BindFlags.RenderTarget;
        using var output=new Texture2D(device,td); using var rtv=new RenderTargetView(device,output);
        td.Usage=ResourceUsage.Staging; td.BindFlags=BindFlags.None; td.CpuAccessFlags=CpuAccessFlags.Read;
        using var staging=new Texture2D(device,td);
        using var vertexCode=ShaderBytecode.Compile(Vertex,"VS","vs_5_0");
        using var vs=new VertexShader(device,vertexCode); using var ps=new PixelShader(device,pixelCode);
        Func<bool,RawVector4> draw=foreground => {
            native.InputAssembler.InputLayout=null; native.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
            native.VertexShader.Set(vs); native.PixelShader.Set(ps);
            native.PixelShader.SetShaderResource(0,atlasSrv); native.PixelShader.SetShaderResource(1,foreground?frontSrv:depthSrv);
            native.PixelShader.SetShaderResource(30,billboardSrv);
            native.Rasterizer.SetViewport(0,0,1,1); native.OutputMerger.SetTargets(rtv);
            native.ClearRenderTargetView(rtv,new RawColor4(0,0,0,0)); native.Draw(3,0); native.OutputMerger.SetTargets((RenderTargetView)null);
            if (deferred) { using var list=native.FinishCommandList(true); device.ImmediateContext.ExecuteCommandList(list,false); }
            device.ImmediateContext.CopyResource(output,staging);
            var box=device.ImmediateContext.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
            try { return System.Runtime.InteropServices.Marshal.PtrToStructure<RawVector4>(box.DataPointer); }
            finally { device.ImmediateContext.UnmapSubresource(staging,0); }
        };
        TransparentStageBindings.RestoreBillboardResources(rc);
        var baseline=draw(false); check(baseline.X==.25f && baseline.W==1,"Native billboard shader healthy baseline: " + baseline);
        rc.PixelShader.SetConstantBuffer(0,null);
        var missingFrame=draw(false); check(missingFrame.W==0,"Native soft particle disappears with missing PS frame");
        TransparentStageBindings.RestoreBillboardResources(rc);
        int samplerSlot=-1;
        for (int i=0;i<reflection.Description.BoundResources;i++) {
            var binding=reflection.GetResourceBindingDescription(i);
            if (binding.Name=="LinearSampler") samplerSlot=binding.BindPoint;
        }
        if (samplerSlot<0) throw new Exception("Native LinearSampler not reflected");
        rc.PixelShader.SetSampler(samplerSlot,null);
        check(draw(false).W==0,"Native atlas sampling disappears with missing PS sampler");
        TransparentStageBindings.RestoreBillboardResources(rc);
        var restored=draw(false);
        check(restored.X==baseline.X && restored.Y==baseline.Y && restored.Z==baseline.Z && restored.W==baseline.W,"Billboard consumer restoration recovers exact native pixels");
        check(draw(true).W==0,"Restored native soft particles still clip foreground depth");
        native.ClearState(); device.ImmediateContext.ClearState();
        Console.WriteLine((deferred?"Deferred":"Immediate")+": actual Keen SOFT_PARTICLE shader, missing frame/sampler invisible, restored pixels exact, foreground clipped");
    }
}
