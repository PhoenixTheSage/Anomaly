param([string]$GameBin = 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64')
$ErrorActionPreference = 'Stop'
# Windows PowerShell hosts the game's .NET Framework SharpDX assemblies.
$root = Split-Path $PSScriptRoot -Parent
$references = @('SharpDX.dll','SharpDX.Direct3D11.dll','SharpDX.D3DCompiler.dll','SharpDX.DXGI.dll','VRage.Math.dll','netstandard.dll') | ForEach-Object { Join-Path $GameBin $_ }
foreach ($assembly in $references) { Add-Type -Path $assembly }
$references += @(Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8\Facades\*.dll" | Where-Object Name -ne 'netstandard.dll' | ForEach-Object FullName)
Add-Type -IgnoreWarnings -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using M = VRageMath.Matrix;
using V = VRageMath.Vector3;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
public static class PointShadowProjectionProbe
{
    static string Function(string source, string name)
    {
        int nameAt=source.IndexOf(name+"(",StringComparison.Ordinal);
        int start=source.LastIndexOf('\n',nameAt)+1;
        int body=source.IndexOf('{',nameAt), depth=1, end=body+1;
        for(;depth>0;end++) { if(source[end]=='{') depth++; else if(source[end]=='}') depth--; }
        return source.Substring(start,end-start)+"\n";
    }
    public static void Run(string helper)
    {
        var forward = new[] {V.Right,V.Left,V.Up,V.Down,V.Backward,V.Forward};
        var up = new[] {V.Down,V.Down,V.Backward,V.Forward,V.Down,V.Down};
        const int count = 600;
        var points = new V[count];
        var random = new Random(917);
        var source = new StringBuilder(File.ReadAllText(helper));
        // Compile the actual shared projection helpers with a minimal frame.
        // Independent CPU cube tests below use the real VRage matrix library.
        source.Append(@"
struct ProbeScreen { float2 resolution; float2 offset; };
struct ProbeEnvironment { float4x4 projection_matrix; };
struct ProbeFrame { ProbeScreen Screen; ProbeEnvironment Environment; };
static ProbeFrame frame_;
float2 screen_to_uv(float2 pixel) { return (pixel-frame_.Screen.offset)/frame_.Screen.resolution; }
float3 compute_screen_ray(float2 uv) {
    return float3((2*uv.x-1+frame_.Environment.projection_matrix._31)/frame_.Environment.projection_matrix._11,
        (1-2*uv.y+frame_.Environment.projection_matrix._32)/frame_.Environment.projection_matrix._22,-1);
}
");
        var fullscreen=File.ReadAllText(Path.Combine(Path.GetDirectoryName(helper),"AnomalyFullscreen.hlsli"));
        foreach(var name in new[] {"AnomalyLightingUv","AnomalyScreenUvToTexel","AnomalyLightingViewPos","AnomalyLightingViewPosUnjittered","AnomalyViewToLightingUv","AnomalyViewToDepthUv"})
            source.Append(Function(fullscreen,name));
        source.Append("\nTexture2D Atlas : register(t0);\nRWStructuredBuffer<float4> Results : register(u0);\nstatic const float3 points[600] = {\n");
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        for (int i=0; i<count; i++)
        {
            int face=i/100;
            var right=V.Cross(up[face],-forward[face]);
            points[i]=forward[face]+right*(float)(random.NextDouble()*1.98-.99)+up[face]*(float)(random.NextDouble()*1.98-.99);
            if (i % 100 < 4)
                points[i] = forward[face] + right * ((i % 2 == 0 ? -1 : 1) * .999999f)
                    + up[face] * ((i % 4 < 2 ? -1 : 1) * .999999f);
            source.AppendFormat(culture,"float3({0:R},{1:R},{2:R}){3}\n",points[i].X,points[i].Y,points[i].Z,i==count-1?"":",");
        }
        source.Append("};\n[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID) { frame_.Screen.resolution=float2(1920,1080); frame_.Screen.offset=float2(7,11); frame_.Environment.projection_matrix=(float4x4)0; frame_.Environment.projection_matrix._11=1.3; frame_.Environment.projection_matrix._22=2.1; frame_.Environment.projection_matrix._31=(id.x%7-3.0)/1920; frame_.Environment.projection_matrix._32=(id.x%5-2.0)/1080; float2 pixel=float2(100.5+id.x,200.5+id.x); float3 receiver=AnomalyLightingViewPosUnjittered(pixel,10+id.x); float2 back=AnomalyViewToDepthUv(receiver)*frame_.Screen.resolution; uint face; float2 uv; AnomalyPointShadowFaceUv(points[id.x],face,uv); float v=AnomalyPointShadowVisibility(Atlas,0,points[id.x],face+1.2,0.04)+2*AnomalyPointShadowVisibility(Atlas,1,points[id.x],face+10.8,0.04)+4*AnomalyPointShadowVisibility(Atlas,3,points[id.x],100,0.04); float edge0=AnomalyPointShadowVisibilitySoft(Atlas,2,float3(-0.5,0,1),50,0); float edge1=AnomalyPointShadowVisibilitySoft(Atlas,2,float3(0,0,1),50,0); float edge2=AnomalyPointShadowVisibilitySoft(Atlas,2,float3(0.5,0,1),50,0); if(abs(edge0-0.125)>0.0001 || abs(edge1-0.5)>0.0001 || abs(edge2-0.875)>0.0001) v=-2; if(AnomalyPointShadowVisibility(Atlas,2,float3(-0.5,0,1),50,0)!=0 || AnomalyPointShadowVisibility(Atlas,2,float3(0,0,1),50,0)!=1 || AnomalyPointShadowVisibility(Atlas,2,float3(0.5,0,1),50,0)!=1) v=-3; Results[id.x]=float4(uv,face,any(abs(back-pixel)>0.002)?-1:v); }");
        using (var pixels = new DataStream(24*13*16,true,true))
        {
        for(int y=0;y<13;y++) for(int x=0;x<24;x++)
        {
            // Header is zero; adjacent faces and rows have distinct depths.
            float depth=y>8?(x%4<2?0:100):y==0?0:(y<=4?1:11)+x/4;
            pixels.Write(new VRageMath.Vector4(depth,depth,depth,1));
        }
        pixels.Position=0;
        using (var device = new Device(DriverType.Warp, DeviceCreationFlags.None))
        using (var texture = new Texture2D(device,new Texture2DDescription {Width=24,Height=13,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R32G32B32A32_Float,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource},new DataRectangle(pixels.DataPointer,24*16)))
        using (var srv = new ShaderResourceView(device,texture))
        using (var bytecode = ShaderBytecode.Compile(source.ToString(),"main","cs_5_0"))
        using (var shader = new ComputeShader(device,bytecode))
        using (var output = new Buffer(device,new BufferDescription {SizeInBytes=count*16,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
        using (var uav = new UnorderedAccessView(device,output))
        using (var staging = new Buffer(device,new BufferDescription {SizeInBytes=count*16,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
        {
            var context=device.ImmediateContext;
            context.ComputeShader.Set(shader);
            context.ComputeShader.SetShaderResource(0,srv);
            context.ComputeShader.SetUnorderedAccessView(0,uav);
            context.Dispatch(count,1,1);
            context.ComputeShader.SetUnorderedAccessView(0,null);
            context.CopyResource(output,staging);
            DataStream stream;
            context.MapSubresource(staging,MapMode.Read,MapFlags.None,out stream);
            try
            {
                for(int i=0;i<count;i++)
                {
                    var actual=stream.Read<VRageMath.Vector4>();
                    if(actual.W<0) throw new Exception((actual.W==-1 ? "Depth reconstruction" : actual.W==-2 ? "Soft shadow tent filter" : "Point shadow edge")+" mismatch at sample "+i);
                    int face=i/100;
                    var view=M.CreateLookAt(V.Zero,forward[face],up[face]);
                    var projection=M.CreatePerspectiveFieldOfView(VRageMath.MathHelper.PiOver2,1,.02f,20);
                    var p=VRageMath.Vector4.Transform(new VRageMath.Vector4(points[i],1),view*projection);
                    float x=p.X/p.W*.5f+.5f, y=.5f-p.Y/p.W*.5f;
                    if(Math.Abs(actual.X-x)>1e-5 || Math.Abs(actual.Y-y)>1e-5 || actual.Z!=face)
                        throw new Exception("Atlas projection mismatch at sample "+i+": actual="+actual+" expected="+x+","+y+","+face);
                    if(Math.Abs(actual.W-6)>1e-5)
                        throw new Exception("Atlas visibility/row isolation mismatch at sample "+i+": actual="+actual.W+" expected=6");
                }
            }
            finally { context.UnmapSubresource(staging,0); stream.Dispose(); }
        }
        }
        Console.WriteLine("PASS: actual HLSL on D3D11 WARP matches VRage cube projection for 600 off-axis directions across all six faces, plus analytical tent-filter edge weights, filtered depth comparisons, face corners, header/row isolation invalid rows and jittered depth reconstruction with screen offsets.");
    }
}
'@
[PointShadowProjectionProbe]::Run((Join-Path $root 'Assets\Shaders\AnomalyPointShadows.hlsli'))




