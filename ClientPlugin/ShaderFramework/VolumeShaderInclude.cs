using System;
using System.Collections.Generic;
using System.IO;
using SharpDX;
using SharpDX.D3DCompiler;
namespace ClientPlugin.ShaderFramework;
internal sealed class VolumeShaderInclude : CallbackBase, Include
{
    readonly string root;
    readonly Dictionary<Stream,string> directories=new();
    internal VolumeShaderInclude(string root) { this.root=root; }
    public Stream Open(IncludeType type,string file,Stream parent)
    {
        string directory=parent!=null && directories.TryGetValue(parent,out var d)?d:root;
        string path=Path.IsPathRooted(file)?file:Path.Combine(directory,file);
        if(!File.Exists(path)) path=Path.Combine(root,file);
        var stream=new MemoryStream(File.ReadAllBytes(path));
        directories[stream]=Path.GetDirectoryName(path); return stream;
    }
    public void Close(Stream stream) { directories.Remove(stream); stream.Dispose(); }
}
