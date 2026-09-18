param([string]$GameBin = 'T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $GameBin 'VRage.Math.dll')
$source = Get-Content (Join-Path $root 'ClientPlugin/ShaderFramework/PointShadowPass.cs') -Raw
$method = [regex]::Match($source, '(?s)    static Matrix CharacterWorldRelativeTo\(.*?\n    \}').Value
if (!$method) { throw 'Production transform helper missing' }
$cube = [regex]::Match($source, '(?s)    static Matrix CubeFaceViewProj\(.*?\n    \}').Value
$test = @'
using System;
using VRageMath;
public static class CharacterTransformProbe {
METHOD
CUBE
const float CubeNear = .02f;
public static void Run() {
 var world = MatrixD.CreateRotationY(.7) * MatrixD.CreateTranslation(new Vector3D(1000000.25, -2000000.5, 3000000.75));
 var vertex = new Vector3(.3f, 1.7f, -.2f);
 var absolute = Vector3D.Transform((Vector3D)vertex, world);
 foreach(double zoom in new[]{0.0,1.0,5.0,20.0,100.0}) {
  var camera = world.Translation + new Vector3D(zoom, 2, zoom * .4);
  var actual = Vector3.Transform(vertex, CharacterWorldRelativeTo(world, camera));
  if(Vector3D.Distance((Vector3D)actual, absolute-camera) > .0001) throw new Exception("Camera-relative transform failed at zoom="+zoom);
 }
 var light = world.Translation + new Vector3D(2, 3, -1);
 var local = CharacterWorldRelativeTo(world, light);
 var lightVertex = Vector3.Transform(vertex, local);
 if(Vector3D.Distance(lightVertex, absolute-light) > .0001) throw new Exception("Light-relative transform failed");
 for(int face=0;face<6;face++) {
  var projection = CubeFaceViewProj(Vector3.Zero,face,20);
  var expectedClip = Vector4.Transform(new Vector4(lightVertex,1),projection);
  for(int orbit=0;orbit<36;orbit++) {
   var angle=orbit*Math.PI/18;
   var camera=world.Translation+new Vector3D(Math.Cos(angle)*100,2,Math.Sin(angle)*100);
   // Independent double-precision reference cancels the observer before projection.
   var relative=(absolute-camera)-(light-camera);
   var referenceClip=Vector4.Transform(new Vector4((Vector3)relative,1),projection);
   if(Vector4.Distance(expectedClip,referenceClip)>.001) throw new Exception("Atlas orbit invariance failed");
  }
 }
 Console.WriteLine("PASS: light-relative cube projection matches camera-independent reference for 36 observer positions on all six faces.");
 Console.WriteLine("PASS: production character transform preserves rotation and position across camera zoom at million-metre world coordinates.");
}
}
'@
$test = $test.Replace('METHOD', $method).Replace('CUBE', $cube)
$refs = @((Join-Path $GameBin 'VRage.Math.dll'), (Join-Path $GameBin 'netstandard.dll'))
$refs += @(Get-ChildItem "$env:USERPROFILE/.nuget/packages/microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/Facades/*.dll" | Where-Object Name -ne 'netstandard.dll' | ForEach-Object FullName)
Add-Type -TypeDefinition $test -ReferencedAssemblies $refs
[CharacterTransformProbe]::Run()
