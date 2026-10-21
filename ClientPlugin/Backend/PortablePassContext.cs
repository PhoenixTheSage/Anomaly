using System;
using System.Collections.Generic;
using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

/// <summary>Renderer-free callback state. Commands are copied when the callback returns.</summary>
public sealed class PortablePassContext
{
    readonly List<object[]> commands = new List<object[]>();
    bool closed;
    internal PortablePassContext(string slot,int phase,int policy,int width,int height,uint frame)
    {Slot=slot;Phase=phase;Policy=policy;Width=width;Height=height;FrameIndex=frame;}
    public string Slot { get; }
    public int Phase { get; }
    public int Policy { get; }
    public int Width { get; }
    public int Height { get; }
    public uint FrameIndex { get; }
    public bool IsOutputResolution => Slot=="AfterUpscale";
    public object Image(string catalogName)=>ClientPlugin.Buffers.BufferCatalog.Active(catalogName);
    public void Fullscreen(string programId,string targetCatalogName=null,string sceneCatalogName=null)
        =>Queue(new object[]{0,programId,targetCatalogName,sceneCatalogName});
    public void Copy(string sourceCatalogName,string targetCatalogName)=>Queue(new object[]{1,sourceCatalogName,targetCatalogName});
    public void Clear(string targetCatalogName,float r,float g,float b,float a)
    {
        foreach(float value in new[]{r,g,b,a})if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));
        Queue(new object[]{2,targetCatalogName,new[]{r,g,b,a}});
    }
    public void ContributeVelocity(string overlayCatalogName,string maskCatalogName)
    {
        if((Policy&2)==0)throw new InvalidOperationException("Portable velocity contribution requires ContributeVelocity policy.");
        Queue(new object[]{3,overlayCatalogName,maskCatalogName});
    }
    void Queue(object[] command)
    {
        if(closed)throw new InvalidOperationException("Portable callback context has expired.");
        if(command.Length<2 || !(command[1] is string name) || string.IsNullOrWhiteSpace(name))throw new ArgumentException("Portable command requires an image or program name.");
        commands.Add(command);
    }
    internal object[][] Finish(){closed=true;return commands.ToArray();}
}
