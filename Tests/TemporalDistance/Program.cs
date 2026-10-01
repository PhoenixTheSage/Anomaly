using System;
using System.Collections;
using System.IO;
using System.Reflection;

static class Program
{
 static int checks;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static object NewSpec(Type type,string id,string file)
 {
  var spec=Activator.CreateInstance(type,true);type.GetField("Id").SetValue(spec,id);type.GetField("File").SetValue(spec,file);return spec;
 }
 static void Main()
 {
  const string bin="T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64";
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{var path=Path.Combine(bin,new AssemblyName(e.Name).Name+".dll");return File.Exists(path)?Assembly.LoadFrom(path):null;};
  Run();
 }
 static void Run()
 {
  var registry=typeof(ClientPlugin.Shaders.FullscreenPassRegistry);
  var specType=registry.Assembly.GetType("ClientPlugin.Shaders.FullscreenProgramSpec",true);
  var add=registry.GetMethod("TryAddUnlocked",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
  var list=(IList)registry.GetField("Programs",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).GetValue(null);
  string path=Path.GetTempFileName();
  try
  {
   Check(ClientPlugin.Shaders.FullscreenPassRegistry.SetVelocityDistanceScale("scaled",1000),"Opt-in registration rejected");
   add.Invoke(null,new[]{NewSpec(specType,"legacy",path)});add.Invoke(null,new[]{NewSpec(specType,"scaled",path)});
   float Scale(object p)=>(float)p.GetType().GetField("VelocityDistanceScale").GetValue(p);
   Check(Scale(list[0])==1&&Scale(list[1])==1000,"Legacy default or pre-registration scale incorrect");
   Check(ClientPlugin.Shaders.FullscreenPassRegistry.SetVelocityDistanceScale("legacy",25)&&Scale(list[0])==25,"Live program update failed");
   foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,0f,.5f,1000001f})
    Check(!ClientPlugin.Shaders.FullscreenPassRegistry.SetVelocityDistanceScale("legacy",invalid)&&Scale(list[0])==25,"Invalid scale mutated live program");
   Check(!ClientPlugin.Shaders.FullscreenPassRegistry.SetVelocityDistanceScale("  ",1000),"Empty program id accepted");
   list.Clear();add.Invoke(null,new[]{NewSpec(specType,"scaled",path)});Check(Scale(list[0])==1000,"Pack reload lost decode scale");
   registry.GetMethod("Release",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,null);
   Check(Scale(list[0])==1000,"Device-end release lost decode scale");
   Check(ClientPlugin.Shaders.FullscreenPassRegistry.SetVelocityDistanceScale("scaled",1)&&Scale(list[0])==1,"Opt-out did not restore raw metres");
   var participation=registry.Assembly.GetType("ClientPlugin.ShaderFramework.TemporalParticipation",true);
   var cb=participation.GetNestedType("IsolatedVelocityConstants",BindingFlags.NonPublic|BindingFlags.Public);
   Check(System.Runtime.InteropServices.Marshal.SizeOf(cb)==256,"Velocity CB allocation size changed");
   Check(System.Runtime.InteropServices.Marshal.OffsetOf(cb,"DistanceScale").ToInt32()==196,"DistanceScale does not reuse padding at byte 196");
  }
  finally{File.Delete(path);}
  Console.WriteLine("PASS: "+checks+" compiled Anomaly temporal-contract assertions, runtime "+Environment.Version);
 }
}
