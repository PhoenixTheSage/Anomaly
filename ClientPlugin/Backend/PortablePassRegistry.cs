using System;
using System.Collections.Generic;
using ClientPlugin.Shaders;

namespace ClientPlugin.Backend;

internal static class PortablePassRegistry
{
    internal sealed class Registration
    {
        internal string Id,Slot;
        internal int Mark,Phase,Priority,Policy;
        internal Action<object> Callback;
        internal string Error;
    }
    static readonly object Gate=new object();
    static readonly List<Registration> Passes=new List<Registration>();
    internal static void Register(string id,string slot,int priority,int policy,Action<object> callback,int phase)
    {
        int mark=GuestMark.FromSlot(slot);
        if(string.IsNullOrWhiteSpace(id)||mark==0||callback==null||(phase!=0&&phase!=1)||(policy&~15)!=0)throw new ArgumentException("Invalid portable pass registration.");
        lock(Gate){Passes.RemoveAll(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase));Passes.Add(new Registration{Id=id,Slot=slot,Mark=mark,Phase=phase,Priority=priority,Policy=policy,Callback=callback});}
        if(RenderHost.IsDx11)BindDx11(id);
    }
    internal static void BindDx11(string id=null)
    {
        Registration[] snapshot;lock(Gate)snapshot=Passes.FindAll(p=>id==null||p.Id==id).ToArray();
        foreach(var pass in snapshot)
            Dx11Call.Call("ClientPlugin.Shaders.OwnedPassRegistry","Register",pass.Id,pass.Slot,pass.Priority,pass.Policy,
                (Action<object>)(native=>InvokeDx11(pass,native)),pass.Phase);
    }
    static void InvokeDx11(Registration pass,object native)
    {
        int width=(int)native.GetType().GetProperty("Width").GetValue(native),height=(int)native.GetType().GetProperty("Height").GetValue(native);
        var context=new PortablePassContext(pass.Slot,pass.Phase,pass.Policy,width,height,FrameTemporal.FrameIndex);
        try
        {
            pass.Callback(context);var commands=context.Finish();
            if(commands.Length!=0 && !Dx11Call.Try("ClientPlugin.Shaders.FullscreenPassRegistry","ExecutePortable",new object[]{native,commands},out _))
                throw new InvalidOperationException("Update Anomaly.Dx11: portable command execution is unavailable.");
            pass.Error=null;
        }
        catch(Exception error){pass.Error=error.GetBaseException().Message;throw;}
        finally{context.Finish();}
    }
    internal static Registration[] Snapshot()
    {
        lock(Gate)
        {
            var result=Passes.ToArray();Array.Sort(result,(a,b)=>{int priority=a.Priority.CompareTo(b.Priority);return priority!=0?priority:string.CompareOrdinal(a.Id,b.Id);});return result;
        }
    }
    internal static void Unregister(string id)
    {
        lock(Gate)Passes.RemoveAll(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase));
        if(RenderHost.IsDx11)Dx11Call.Call("ClientPlugin.Shaders.OwnedPassRegistry","Unregister",id);
    }
    internal static string Error(string id)
    {
        lock(Gate)return Passes.Find(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase))?.Error;
    }
    internal static void Run(Registration[] snapshot,int mark,int phase,IReadOnlyList<GuestProgram> programs)
    {
        foreach(var pass in snapshot)
        {
            if(pass.Mark!=mark || pass.Phase!=phase)continue;
            var dimensions=new GuestProgram{Mark=mark,Scale=1,Compose="Replace"};GuestBridgeClient.PassSize(dimensions,out int width,out int height);
            var context=new PortablePassContext(pass.Slot,phase,pass.Policy,width,height,GuestFrame.Open?GuestFrame.U32(16):0);
            try{pass.Callback(context);GuestBridgeClient.ExecutePortable(mark,context.Finish(),programs);pass.Error=null;}
            catch(Exception error){context.Finish();pass.Error=error.GetBaseException().Message;System.Diagnostics.Trace.WriteLine("Anomaly portable pass "+pass.Id+": "+pass.Error);}
        }
    }
}
