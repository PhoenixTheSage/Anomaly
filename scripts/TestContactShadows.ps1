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
public static class ContactProbe
{
 public static void Run(string path)
 {
  string source = @"
float2 AnomalyViewToDepthUv(float3 p) { return float2(.5+.5*p.x/-p.z,.5-.5*p.y/-p.z); }
float3 AnomalyLightingViewPos(float2 pixel,float z) { float2 uv=pixel/float2(256,64); return float3(2*uv.x-1,1-2*uv.y,-1)*z; }
" + File.ReadAllText(path) + @"
Texture2D<float> Blocker : register(t0);
Texture2D<float> Floor : register(t1);
Texture2D<float> Player : register(t2);
RWStructuredBuffer<float4> Results : register(u0);
[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID) {
 float3 receiver=AnomalyLightingViewPos(float2(id.x+.5,32.5),10);
 float3 toLight=float3(0,0,-2)-receiver;
 float3 L=normalize(toLight);
 float full=AnomalyContactVisibility(Blocker,receiver,float3(0,0,1),L,length(toLight),.2,192);
 float floor=AnomalyContactVisibility(Floor,receiver,float3(0,0,1),L,length(toLight),.2,192);
 float shortRay=AnomalyContactVisibility(Blocker,receiver,float3(0,0,1),L,.5,.2,192);
 float3x3 rotation=float3x3(0,0,-1,0,1,0,1,0,0);
 float excluded=AnomalyContactVisibilityExcludingBounds(Blocker,receiver,float3(0,0,1),L,length(toLight),.2,192,
     float3(-5,0,0),float3(.1,5.1,1.1),rotation);
 float preserved=AnomalyContactVisibilityExcludingBounds(Blocker,receiver,float3(0,0,1),L,length(toLight),.2,192,
     float3(-5,0,8),float3(.1,5.1,1.1),rotation);
 float playerSkipped=AnomalyContactVisibilityExcludingPlayer(Blocker,Player,receiver,float3(0,0,1),L,length(toLight),.2,192);
 // The same pixels occupied by a wall in front of the character must remain.
 float foregroundPreserved=AnomalyContactVisibilityExcludingPlayer(Blocker,Floor,receiver,float3(0,0,1),L,length(toLight),.2,192);
 Results[id.x]=float4(full,floor,shortRay,excluded>.999 && abs(preserved-full)<.0001 && playerSkipped>.999 && abs(foregroundPreserved-full)<.0001 ? 1:0);
}";
  using(var pixels=new DataStream(256*64*4,true,true))
  using(var floorPixels=new DataStream(256*64*4,true,true))
  using(var playerPixels=new DataStream(256*64*4,true,true))
  {
   for(int y=0;y<64;y++) for(int x=0;x<256;x++) { pixels.Write(x>=102 && x<154 ? 5f:10f); floorPixels.Write(10f); }
   for(int y=0;y<64;y++) for(int x=0;x<256;x++) {
    double vx=(2*(x+.5)/256-1)*5, vy=(1-2*(y+.5)/64)*5;
    playerPixels.Write(x>=102 && x<154 ? (float)Math.Sqrt(vx*vx+vy*vy+25):100000f);
   }
   pixels.Position=floorPixels.Position=playerPixels.Position=0;
   var desc=new Texture2DDescription {Width=256,Height=64,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R32_Float,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource};
   using(var device=new Device(DriverType.Warp,DeviceCreationFlags.None))
   using(var texture=new Texture2D(device,desc,new DataRectangle(pixels.DataPointer,256*4)))
   using(var floorTexture=new Texture2D(device,desc,new DataRectangle(floorPixels.DataPointer,256*4)))
   using(var playerTexture=new Texture2D(device,desc,new DataRectangle(playerPixels.DataPointer,256*4)))
   using(var playerSrv=new ShaderResourceView(device,playerTexture))
   using(var srv=new ShaderResourceView(device,texture))
   using(var floorSrv=new ShaderResourceView(device,floorTexture))
   using(var bc=ShaderBytecode.Compile(source,"main","cs_5_0"))
   using(var shader=new ComputeShader(device,bc))
   using(var output=new Buffer(device,new BufferDescription {SizeInBytes=256*16,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
   using(var uav=new UnorderedAccessView(device,output))
   using(var staging=new Buffer(device,new BufferDescription {SizeInBytes=256*16,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
   {
    var context=device.ImmediateContext;
    context.ComputeShader.Set(shader); context.ComputeShader.SetShaderResource(0,srv); context.ComputeShader.SetShaderResource(1,floorSrv); context.ComputeShader.SetShaderResource(2,playerSrv); context.ComputeShader.SetUnorderedAccessView(0,uav);
    context.Dispatch(256,1,1); context.ComputeShader.SetUnorderedAccessView(0,null); context.CopyResource(output,staging);
    DataStream stream; context.MapSubresource(staging,MapMode.Read,MapFlags.None,out stream);
    try {
     for(int x=0;x<256;x++) {
      var v=stream.Read<VRageMath.Vector4>();
      if(v.W<.999) throw new Exception("Caster exclusion or unrelated-blocker preservation failed at "+x+": "+v);
      if (v.Y<.999 || v.Z<.999) throw new Exception("Self-shadow or hit beyond ray endpoint at "+x+": "+v);
      if ((x<88 || x>167) && v.X<.999) throw new Exception("False shadow outside blocker at "+x+": "+v);
      if ((x>=96 && x<=101 || x>=154 && x<=159) && v.X>.2) throw new Exception("Repeated gap inside contiguous blocker shadow at "+x+": "+v);
     }
    } finally { context.UnmapSubresource(staging,0); stream.Dispose(); }
   }
  }
  Console.WriteLine("PASS: 256 D3D11 WARP contact rays: contiguous blocker shadow, no receiver-plane self-shadow, no hits beyond the light-ray endpoint, rotated bounds exclusion, per-pixel player exclusion, and foreground-wall preservation.");
 }
}
'@
[ContactProbe]::Run((Join-Path $root 'Assets\Shaders\AnomalyContactShadows.hlsli'))
