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
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
public static class ContactDistanceProbe
{
 public static void Run(string path, float cameraOffset, float worldScale)
 {
  string source = "static const float CameraOffset = " + cameraOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; static const float WorldScale = " + worldScale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";\n" + @"
float2 AnomalyViewToDepthUv(float3 p) { return float2(.5+.5*p.x/-p.z,.5-.5*p.y/-p.z); }
float3 AnomalyLightingViewPos(float2 pixel,float z) { float2 uv=pixel/float2(256,64); return float3(2*uv.x-1,1-2*uv.y,-1)*z; }
" + File.ReadAllText(path) + @"
Texture2D<float> Blocker : register(t0);
Texture2D<float> Floor : register(t1);
RWStructuredBuffer<float4> Results : register(u0);
[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID) {
 float3 receiver=float3((2.1+id.x*.001)*WorldScale,(id.x%2)*2.1*WorldScale,-10*WorldScale-CameraOffset);
 float3 toLight=float3(0,0,-2*WorldScale-CameraOffset)-receiver;
 float3 L=normalize(toLight);
 float full=AnomalyContactVisibility(Blocker,receiver,float3(0,0,1),L,length(toLight),.2*WorldScale,192);
 float floor=AnomalyContactVisibility(Floor,receiver,float3(0,0,1),L,length(toLight),.2*WorldScale,192);
 float shortRay=AnomalyContactVisibility(Blocker,receiver,float3(0,0,1),L,.5*WorldScale,.2*WorldScale,192);
 float3 outside=float3(5*WorldScale,0,-10*WorldScale-CameraOffset);
 float3 outsideLight=float3(0,0,-2*WorldScale-CameraOffset)-outside;
 float clear=AnomalyContactVisibility(Blocker,outside,float3(0,0,1),normalize(outsideLight),length(outsideLight),.2*WorldScale,192);
 Results[id.x]=float4(full,floor,shortRay,clear);
}";
  using(var pixels=new DataStream(256*64*4,true,true))
  using(var floorPixels=new DataStream(256*64*4,true,true))
  {
   for(int y=0;y<64;y++) for(int x=0;x<256;x++) { float blockerX=(2*(x+.5f)/256-1)*(5*worldScale+cameraOffset); pixels.Write(Math.Abs(blockerX)<=worldScale ? 5f*worldScale+cameraOffset:10f*worldScale+cameraOffset); floorPixels.Write(10f*worldScale+cameraOffset); }
   pixels.Position=floorPixels.Position=0;
   var desc=new Texture2DDescription {Width=256,Height=64,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R32_Float,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource};
   using(var device=new Device(DriverType.Warp,DeviceCreationFlags.None))
   using(var texture=new Texture2D(device,desc,new DataRectangle(pixels.DataPointer,256*4)))
   using(var floorTexture=new Texture2D(device,desc,new DataRectangle(floorPixels.DataPointer,256*4)))
   using(var srv=new ShaderResourceView(device,texture))
   using(var floorSrv=new ShaderResourceView(device,floorTexture))
   using(var bc=ShaderBytecode.Compile(source,"main","cs_5_0"))
   using(var shader=new ComputeShader(device,bc))
   using(var output=new Buffer(device,new BufferDescription {SizeInBytes=256*16,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
   using(var uav=new UnorderedAccessView(device,output))
   using(var staging=new Buffer(device,new BufferDescription {SizeInBytes=256*16,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
   {
    var context=device.ImmediateContext;
    context.ComputeShader.Set(shader); context.ComputeShader.SetShaderResource(0,srv); context.ComputeShader.SetShaderResource(1,floorSrv); context.ComputeShader.SetUnorderedAccessView(0,uav);
    context.Dispatch(256,1,1); context.ComputeShader.SetUnorderedAccessView(0,null); context.CopyResource(output,staging);
    DataStream stream; context.MapSubresource(staging,MapMode.Read,MapFlags.None,out stream);
    try {
     for(int x=0;x<256;x++) {
      var v=stream.Read<VRageMath.Vector4>();
      if(v.X>.2 || v.Y<.999 || v.Z<.999 || v.W<.999) throw new Exception("Camera-distance regression offset="+cameraOffset+" receiver="+x+" result="+v);

     }
    } finally { context.UnmapSubresource(staging,0); stream.Dispose(); }
   }
  }
  Console.WriteLine("PASS: 256 fixed-world receiver rays, camera offset="+cameraOffset+"m, scene scale="+worldScale+"; stable blocker, flat receiver and finite endpoints.");
 }
}
'@
foreach ($scale in @(1.0, 0.2)) { foreach ($offset in @(0, 5, 15, 30, 40)) { [ContactDistanceProbe]::Run((Join-Path $root 'Assets\Shaders\AnomalyContactShadows.hlsli'), ($offset * $scale), $scale) } }
