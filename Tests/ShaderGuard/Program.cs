using ClientPlugin.ShaderFramework;
int formatted=0;
ClientPlugin.Shaders.VolumetricMediumRegistry.SetActiveStatus(()=> { formatted++; return "active"; });
if(formatted!=0) throw new Exception("status formatted eagerly");
if(ClientPlugin.Shaders.VolumetricMediumRegistry.StatusLine!="active" || formatted!=1) throw new Exception("status not formatted on read");
ClientPlugin.Shaders.VolumetricMediumRegistry.SetStatus("fallback");
if(ClientPlugin.Shaders.VolumetricMediumRegistry.StatusLine!="fallback" || formatted!=1) throw new Exception("stale active status after fallback");
ClientPlugin.Shaders.VolumetricMediumRegistry.SetActiveStatus(()=> { formatted++; return "active"; });
ClientPlugin.Shaders.VolumetricMediumRegistry.ResetIntervals();
_ = ClientPlugin.Shaders.VolumetricMediumRegistry.StatusLine;
if(formatted!=1) throw new Exception("stale active status after reset");
Console.WriteLine("PASS lazy volume status and invalidation");
ClientPlugin.RichHud.TerminalCategoryNest.EnsurePatched();
var parent=new RichHudFramework.UI.Server.TerminalPageCategoryBase();
var child=new RichHudFramework.UI.Server.TerminalPageCategoryBase();
var leaf=new object(); child.DirectPage=leaf;
if(!ClientPlugin.RichHud.TerminalCategoryNest.TryAttach(new(parent),new(child))) throw new Exception("attach failed");
if(!ReferenceEquals(parent.SelectedPage,leaf)) throw new Exception("owned nested page unresolved");
var other=new RichHudFramework.UI.Server.TerminalPageCategoryBase(); other.Tree.Add(child);
if(other.SelectedPage!=null || other.Tree.Reads!=0) throw new Exception("unowned category inspected");
for(int i=0;i<20;i++) if(!ReferenceEquals(parent.SelectedPage,leaf)) throw new Exception("cached selection failed");
Console.WriteLine("PASS owned nesting recursion, repeated selection, unrelated client isolation");
var original=args[0];
var fixture=Path.Combine(Path.GetTempPath(),"AnomalyShaderGuard-"+Guid.NewGuid());
var files=new[]{"Lighting/Light.hlsli","Transparent/Atmosphere/AtmosphereCommon.hlsli","Geometry/Passes/GBuffer/VertexStage.hlsli","Geometry/Passes/GBuffer/PixelStage.hlsli","GBuffer/GBufferWrite.hlsli","GBuffer/GBuffer.hlsli"};
void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS "+name); }
try {
 foreach(var file in files) { var dest=Path.Combine(fixture,file); Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(Path.Combine(original,file),dest); }
 VRageRender.MyShaderCompiler.ShadersPath=fixture;
 KeenShaderGuard.Prepare();
 Check(KeenShaderGuard.GBufferPatchesReady,"installed GBuffer baseline accepted");
 Check(!KeenShaderGuard.IsWrapperBlocked(files[0]) && !KeenShaderGuard.IsWrapperBlocked(files[1]),"both wrapper baselines accepted");
 File.AppendAllText(Path.Combine(fixture,files[0]),"// changed slot map"); KeenShaderGuard.Prepare();
 Check(KeenShaderGuard.IsWrapperBlocked(files[0]) && !KeenShaderGuard.IsWrapperBlocked(files[1]),"lighting mutation disables only lighting wrapper");
 Check(KeenShaderGuard.GBufferPatchesReady,"wrapper mismatch preserves valid GBuffer");
 File.Delete(Path.Combine(fixture,files[1])); KeenShaderGuard.Prepare();
 Check(KeenShaderGuard.IsWrapperBlocked(files[1]),"missing atmosphere original fails closed");
 Check(!KeenShaderGuard.IsWrapperBlocked("Other/Shader.hlsli"),"unrelated includes unaffected");
} finally { Directory.Delete(fixture,true); }
namespace VRageRender { static class MyShaderCompiler { public static string ShadersPath; } }
namespace VRage.Utils { class MyLog { public static MyLog Default=new(); public void WriteLine(string s) { } public void Warning(string s) { } } }
namespace ClientPlugin.ShaderFramework { static class DebugLog { public static void Write(string s) { } } }

namespace RichHudFramework.UI.Client
{
    class TerminalPageCategory { public object ID { get; } public TerminalPageCategory(object id) { ID=id; } }
}
namespace RichHudFramework.UI.Server
{
    class Tree
    {
        object selected;
        public int Reads;
        public object Value { get { Reads++; return selected; } }
        public void Add(TerminalPageCategoryBase child) { selected=child; }
    }
    class TerminalPageCategoryBase
    {
        readonly Tree treeBox=new();
        public Tree Tree=>treeBox;
        public object DirectPage;
        public object SelectedPage { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { return DirectPage; } }
    }
}
