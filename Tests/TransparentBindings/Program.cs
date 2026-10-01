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

static class Program
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    const string Probe = @"
cbuffer Frame : register(b0) { float4 FrameTint; }
Texture2D<float4> Atlas : register(t0); SamplerState AtlasSampler : register(s1);
float4 VS(uint id : SV_VertexID) : SV_Position {
  float2 p = float2((id << 1) & 2, id & 2); return float4(p * float2(2,-2) + float2(-1,1),0,1);
}
float4 PS(float4 p : SV_Position) : SV_Target { return FrameTint * Atlas.SampleLevel(AtlasSampler,float2(1.25,.5),0); }
";
    static RawVector4 Read(Device device, Texture2D texture)
    {
        var desc = texture.Description; desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None; desc.CpuAccessFlags = CpuAccessFlags.Read;
        using var staging = new Texture2D(device, desc);
        device.ImmediateContext.CopyResource(texture, staging);
        DataStream stream;
        device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out stream);
        try { return stream.Read<RawVector4>(); }
        finally { device.ImmediateContext.UnmapSubresource(staging, 0); stream.Dispose(); }
    }
    static void Run(Device device, bool deferred)
    {
        using var context = deferred ? new DeviceContext(device) : null;
        var native = context ?? device.ImmediateContext;
        var rc = new MyRenderContext(native);
        using var frameData = new DataStream(16, true, true);
        frameData.Write(new RawVector4(.25f, .5f, .75f, 1)); frameData.Position = 0;
        using var frame = new Buffer(device, frameData, new BufferDescription(16, ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        using var other = new Buffer(device, frame.Description);
        MyCommon.FrameConstants = frame;
        var samplerDesc = new SamplerStateDescription { Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, MaximumLod = float.MaxValue };
        using var standard = new SamplerState(device, samplerDesc);
        samplerDesc.AddressU = samplerDesc.AddressV = samplerDesc.AddressW = TextureAddressMode.Clamp;
        samplerDesc.Filter = Filter.MinMagMipPoint;
        using var changed = new SamplerState(device, samplerDesc);
        MySamplerStateManager.StandardSamplers = new[] { standard, standard, standard };
        MySamplerStateManager.Shadowmap = standard;
        using var texData = new DataStream(32, true, true);
        texData.Write(new RawVector4(1,0,0,1)); texData.Write(new RawVector4(0,1,0,1)); texData.Position = 0;
        var td = new Texture2DDescription { Width = 2, Height = 1, MipLevels = 1, ArraySize = 1, Format = Format.R32G32B32A32_Float, SampleDescription = new SampleDescription(1,0), Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource };
        using var atlas = new Texture2D(device, td, new DataRectangle(texData.DataPointer,32));
        using var srv = new ShaderResourceView(device, atlas);
        td.Width = 1; td.Usage = ResourceUsage.Default; td.BindFlags = BindFlags.RenderTarget;
        using var output = new Texture2D(device, td); using var rtv = new RenderTargetView(device, output);
        using var vsCode = ShaderBytecode.Compile(Probe,"VS","vs_5_0"); using var psCode = ShaderBytecode.Compile(Probe,"PS","ps_5_0");
        using var vs = new VertexShader(device,vsCode); using var ps = new PixelShader(device,psCode);
        Func<RawVector4> draw = () => {
            native.InputAssembler.InputLayout = null; native.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
            native.VertexShader.Set(vs); native.PixelShader.Set(ps); native.PixelShader.SetShaderResource(0,srv);
            native.Rasterizer.SetViewport(0,0,1,1); native.OutputMerger.SetTargets(rtv); native.Draw(3,0);
            native.OutputMerger.SetTargets((RenderTargetView)null);
            if (deferred) { using var list = native.FinishCommandList(true); device.ImmediateContext.ExecuteCommandList(list,false); }
            return Read(device,output);
        };
        Action leak = () => {
            rc.VertexShader.SetConstantBuffer(0,other); rc.PixelShader.SetConstantBuffer(0,other);
            rc.PixelShader.SetSampler(0,changed); rc.PixelShader.SetSampler(1,changed); rc.PixelShader.SetSampler(2,changed);
            rc.PixelShader.SetSampler(15,null); rc.PixelShader.SetConstantBuffer(0,null);
        };
        TransparentStageBindings.RestoreAfterAtmosphere(rc);
        var baseline = draw(); Check(baseline.X == .25f && baseline.Y == 0 && baseline.W == 1,"Healthy inherited binding probe failed");
        leak(); var broken = draw(); Check(broken.X == 0 && broken.W == 0,"Missing frame CB did not reproduce lost emission");
        foreach (bool fail in new[] { false, true })
        {
            try { leak(); if (fail) throw new InvalidOperationException("Injected tenant failure"); }
            catch (InvalidOperationException) { }
            finally { TransparentStageBindings.RestoreAfterAtmosphere(rc); }
            Check(ReferenceEquals(rc.VertexShader.Constants[0],frame) && ReferenceEquals(rc.PixelShader.Constants[0],frame),"Frame CB cache not restored");
            for (int i=0;i<3;i++) Check(ReferenceEquals(rc.PixelShader.Samplers[i],standard),"Sampler cache not restored");
            Check(ReferenceEquals(rc.PixelShader.Samplers[15],standard),"Shadow sampler not restored");
            var restored = draw(); Check(restored.X == baseline.X && restored.Y == baseline.Y && restored.Z == baseline.Z && restored.W == baseline.W,"Native pixel output not restored");
            // A repeated same-reference Keen bind must safely skip without undoing restoration.
            rc.PixelShader.SetConstantBuffer(0,frame); var repeated = draw();
            Check(repeated.X == baseline.X && repeated.W == baseline.W,"Native/cache restoration disagreed on repeated binding");
        }
        native.ClearState(); device.ImmediateContext.ClearState();
        Console.WriteLine((deferred ? "Deferred" : "Immediate") + ": missing b0 zeros emission; restored frame/samplers recover exact probe pixels");
    }
    static void Main()
    {
        using var device = new Device(DriverType.Warp,DeviceCreationFlags.None);
        Run(device,false); Run(device,true);
        Console.WriteLine("PASS: " + checks + " transparent binding assertions; runtime " + Environment.Version);
    }
}
