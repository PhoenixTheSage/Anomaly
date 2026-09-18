using System;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>Render-thread owned float volumes. No product survives a frame or device epoch implicitly.</summary>
internal sealed class VolumetricFrameResources : IDisposable
{
    internal const int Version = 1;
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal int Slices { get; private set; }
    internal long Epoch { get; private set; }
    internal uint Frame { get; private set; }
    internal bool Ready { get; private set; }
    internal Volume Coefficients, Source, ScatteringTransmittance, History, Motion, SunVisibility;
    internal Texture2D RepresentativeDepth, ReactiveCoverage;
    internal ShaderResourceView DepthSrv, ReactiveSrv;
    internal UnorderedAccessView DepthUav, ReactiveUav;

    internal bool Matches(uint frame, long epoch) => Ready && Frame == frame && Epoch == epoch;
    internal void BeginFrame() { Ready = false; }
    internal void Publish(uint frame)
    {
        if (Coefficients == null) throw new InvalidOperationException("Cannot publish released volume resources");
        Frame = frame; Ready = true;
    }

    internal void Ensure(int renderWidth, int renderHeight, int columnPixels, int slices)
    {
        if (renderWidth <= 0 || renderHeight <= 0 || columnPixels < 4 || slices < 16 || slices > 128)
            throw new ArgumentOutOfRangeException(nameof(renderWidth));
        int width = (renderWidth + columnPixels - 1)/columnPixels;
        int height = (renderHeight + columnPixels - 1)/columnPixels;
        if (width == Width && height == Height && slices == Slices && Coefficients != null) return;
        Dispose();
        try
        {
            Coefficients = new Volume(width,height,slices,Format.R16G16B16A16_Float);
            Source = new Volume(width,height,slices,Format.R16G16B16A16_Float);
            ScatteringTransmittance = new Volume(width,height,slices,Format.R16G16B16A16_Float);
            History = new Volume(width,height,slices,Format.R16G16B16A16_Float);
            Motion = new Volume(width,height,slices,Format.R16G16B16A16_Float);
            SunVisibility = new Volume(width,height,slices,Format.R16_Float);
            RepresentativeDepth = New2D(width,height,Format.R32_Float);
            ReactiveCoverage = New2D(width,height,Format.R16_Float);
            DepthSrv = new ShaderResourceView(MyRender11.DeviceInstance,RepresentativeDepth);
            DepthUav = new UnorderedAccessView(MyRender11.DeviceInstance,RepresentativeDepth);
            ReactiveSrv = new ShaderResourceView(MyRender11.DeviceInstance,ReactiveCoverage);
            ReactiveUav = new UnorderedAccessView(MyRender11.DeviceInstance,ReactiveCoverage);
            Width=width; Height=height; Slices=slices;
        }
        catch { Dispose(); throw; }
    }

    static Texture2D New2D(int w,int h,Format format) => new(MyRender11.DeviceInstance,new Texture2DDescription {
        Width=w,Height=h,MipLevels=1,ArraySize=1,Format=format,
        SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,
        BindFlags=BindFlags.ShaderResource|BindFlags.UnorderedAccess
    });

    public void Dispose()
    {
        Ready=false; Epoch++; Width=Height=Slices=0;
        Coefficients?.Dispose(); Source?.Dispose(); ScatteringTransmittance?.Dispose();
        History?.Dispose(); Motion?.Dispose(); SunVisibility?.Dispose();
        Coefficients=Source=ScatteringTransmittance=History=Motion=SunVisibility=null;
        DepthSrv?.Dispose(); DepthUav?.Dispose(); ReactiveSrv?.Dispose(); ReactiveUav?.Dispose();
        RepresentativeDepth?.Dispose(); ReactiveCoverage?.Dispose();
        DepthSrv=ReactiveSrv=null; DepthUav=ReactiveUav=null;
        RepresentativeDepth=ReactiveCoverage=null;
    }

    internal sealed class Volume : IDisposable
    {
        internal readonly Texture3D Texture;
        internal readonly ShaderResourceView Srv;
        internal readonly UnorderedAccessView Uav;
        internal Volume(int width,int height,int depth,Format format)
        {
            try
            {
                Texture=new Texture3D(MyRender11.DeviceInstance,new Texture3DDescription {
                    Width=width,Height=height,Depth=depth,MipLevels=1,Format=format,
                    Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource|BindFlags.UnorderedAccess
                });
                Srv=new ShaderResourceView(MyRender11.DeviceInstance,Texture);
                Uav=new UnorderedAccessView(MyRender11.DeviceInstance,Texture);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { Uav?.Dispose(); Srv?.Dispose(); Texture?.Dispose(); }
    }
}
