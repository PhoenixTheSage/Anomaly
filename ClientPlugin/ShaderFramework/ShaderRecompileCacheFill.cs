using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SharpDX.Direct3D;
using VRage.Library.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Parallel ShaderCache2 fill used by Harmony prefixes on Keen Recompile.
/// Does not create or dispose shader objects.
/// </summary>
static class ShaderRecompileCacheFill
{
    static int running;

    internal static bool IsRunning => Volatile.Read(ref running) != 0;

    public static void FillManagers()
    {
        var jobs = new List<ShaderWarmup.Job>(256);
        CollectPixel(jobs);
        CollectVertex(jobs);
        CollectCompute(jobs);
        CollectGeometry(jobs);
        CompileAll(jobs);
    }

    public static void FillMaterials()
    {
        var jobs = new List<ShaderWarmup.Job>(256);
        MyMaterialShadersBundleId[] ids;
        lock (MyMaterialShaders.m_hashIndex)
        {
            ids = new MyMaterialShadersBundleId[MyMaterialShaders.m_hashIndex.Count];
            MyMaterialShaders.m_hashIndex.Values.CopyTo(ids, 0);
        }

        for (var i = 0; i < ids.Length; i++)
        {
            var index = ids[i].Index;
            if (index < 0)
                continue;
            MyMaterialShadersInfo bundle;
            try
            {
                bundle = MyMaterialShaders.BundleInfo.Data[index];
            }
            catch
            {
                continue;
            }

            try
            {
                var macros = new List<ShaderMacro>
                {
                    MyMaterialShaders.GetRenderingPassMacro(bundle.Pass.String)
                };
                MyMaterialShaders.AddMaterialShaderFlagMacrosTo(macros, bundle.Flags, bundle.TextureTypes);
                if (bundle.Layout.Index >= 0)
                {
                    var layoutMacros = bundle.Layout.Info.Macros;
                    if (layoutMacros != null && layoutMacros.Length != 0)
                        macros.AddRange(layoutMacros);
                }

                MyMaterialShaders.GetMaterialSources(bundle.Material, out var sources);
                var arr = macros.ToArray();
                var vsDesc = MyMaterialShaders.GetShaderDescriptor(sources.VertexShaderFilename,
                    bundle.Material.String, bundle.Pass.String, bundle.Layout);
                var psDesc = MyMaterialShaders.GetShaderDescriptor(sources.PixelShaderFilename,
                    bundle.Material.String, bundle.Pass.String, bundle.Layout);
                ShaderWarmup.Enqueue(jobs, sources.VertexShaderFilepath, MyShaderProfile.vs_5_0, vsDesc, arr);
                ShaderWarmup.Enqueue(jobs, sources.PixelShaderFilepath, MyShaderProfile.ps_5_0, psDesc, arr);
            }
            catch (Exception e)
            {
                DebugLog.Write("material cache fill skipped: " + e.Message);
            }
        }

        CompileAll(jobs);
    }

    static void CollectPixel(List<ShaderWarmup.Job> jobs)
    {
        List<MyShaderCompilationInfo> infos;
        lock (MyPixelShaders.m_keyToId)
        {
            infos = new List<MyShaderCompilationInfo>(MyPixelShaders.m_keyToId.Count);
            foreach (var id in MyPixelShaders.m_keyToId.Values)
                AddInfo(MyPixelShaders.m_shaders, id.Index, infos);
        }

        AddInfos(jobs, infos);
    }

    static void CollectVertex(List<ShaderWarmup.Job> jobs)
    {
        List<MyShaderCompilationInfo> infos;
        lock (MyVertexShaders.m_keyToId)
        {
            infos = new List<MyShaderCompilationInfo>(MyVertexShaders.m_keyToId.Count);
            foreach (var id in MyVertexShaders.m_keyToId.Values)
                AddInfo(MyVertexShaders.m_shaders, id.Index, infos);
        }

        AddInfos(jobs, infos);
    }

    static void CollectCompute(List<ShaderWarmup.Job> jobs)
    {
        List<MyShaderCompilationInfo> infos;
        lock (MyComputeShaders.m_keyToId)
        {
            infos = new List<MyShaderCompilationInfo>(MyComputeShaders.m_keyToId.Count);
            foreach (var id in MyComputeShaders.m_keyToId.Values)
                AddInfo(MyComputeShaders.m_shaders, id.Index, infos);
        }

        AddInfos(jobs, infos);
    }

    static void CollectGeometry(List<ShaderWarmup.Job> jobs)
    {
        List<MyShaderCompilationInfo> infos;
        lock (MyGeometryShaders.m_keyToId)
        {
            infos = new List<MyShaderCompilationInfo>(MyGeometryShaders.m_keyToId.Count);
            foreach (var id in MyGeometryShaders.m_keyToId.Values)
            {
                if (id.Index < 0)
                    continue;
                infos.Add(MyShaders.GetCompilationInfo(MyGeometryShaders.m_shaders.Data[id.Index].InfoId));
            }
        }

        AddInfos(jobs, infos);
    }

    static void AddInfo<T>(MyFreelist<MyShaderInfo<T>> shaders, int index, List<MyShaderCompilationInfo> infos)
        where T : class
    {
        if (index < 0)
            return;
        infos.Add(MyShaders.GetCompilationInfo(shaders.Data[index].InfoId));
    }

    static void AddInfos(List<ShaderWarmup.Job> jobs, List<MyShaderCompilationInfo> infos)
    {
        if (infos == null || infos.Count == 0)
            return;
        string root;
        try
        {
            root = MyShaderCompiler.ShadersPath;
        }
        catch
        {
            return;
        }

        for (var i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            var file = info.File.ToString();
            if (string.IsNullOrEmpty(file))
                continue;
            var path = PathUtils.Normalize(Path.Combine(root, file));
            ShaderWarmup.Enqueue(jobs, path, info.Profile, file, info.Macros);
        }
    }

    static void CompileAll(List<ShaderWarmup.Job> jobs)
    {
        if (jobs == null || jobs.Count == 0)
            return;
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            return;
        try
        {
            ShaderCompileParallel.For(0, jobs.Count, i =>
            {
                var job = jobs[i];
                try
                {
                    MyShaderCompiler.Compile(job.Path, job.Macros, job.Profile, job.Descriptor,
                        invalidateCache: false);
                }
                catch (Exception e)
                {
                    DebugLog.Write("Recompile cache fill " + job.Descriptor + ": " + e.Message);
                }
            });
            DebugLog.Write("Recompile cache fill compiled " + jobs.Count + " jobs");
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }
}
