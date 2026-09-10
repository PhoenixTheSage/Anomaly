using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ClientPlugin.Shaders;
using SharpDX.Direct3D;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Fills Keen's shader cache then creates Anomaly / pack programs on the
/// render thread before the first world present, so EnsureShaders is a no-op.
/// </summary>
static class ShaderWarmup
{
    internal readonly struct Job
    {
        public readonly string Path;
        public readonly ShaderMacro[] Macros;
        public readonly MyShaderProfile Profile;
        public readonly string Descriptor;

        public Job(string path, ShaderMacro[] macros, MyShaderProfile profile, string descriptor)
        {
            Path = path;
            Macros = macros ?? Array.Empty<ShaderMacro>();
            Profile = profile;
            Descriptor = descriptor;
        }
    }

    static int pending;

    public static void Request()
    {
        Interlocked.Exchange(ref pending, 1);
    }

    public static void TryRun()
    {
        if (Interlocked.CompareExchange(ref pending, 0, 1) != 1)
            return;
        if (!ShaderCompileIntercept.IsLive || MyRender11.DeviceInstance == null)
        {
            Interlocked.Exchange(ref pending, 1);
            return;
        }

        try
        {
            var jobs = new List<Job>(24);
            FullscreenPassRegistry.CollectWarmupJobs(jobs);
            OwnedBuffersPass.CollectWarmupJobs(jobs);
            CameraVelocityPass.CollectWarmupJobs(jobs);
            TemporalParticipation.CollectWarmupJobs(jobs);
            if (WantDebugShaders())
                VelocityDebugPass.CollectWarmupJobs(jobs);

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
                    MyLog.Default.WriteLine("Anomaly warmup compile " + job.Descriptor + ": " +
                        e.GetType().Name + ": " + e.Message);
                }
            });

            FullscreenPassRegistry.Prewarm();
            OwnedBuffersPass.Prewarm();
            CameraVelocityPass.Prewarm();
            TemporalParticipation.Prewarm();
            if (WantDebugShaders())
                VelocityDebugPass.Prewarm();
            DebugLog.Write("ShaderWarmup compiled " + jobs.Count + " jobs");
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("Anomaly ShaderWarmup failed: " + e.GetType().Name + ": " + e.Message);
            DebugLog.Write("ShaderWarmup failed: " + e);
        }
    }

    static bool WantDebugShaders()
    {
        var cfg = Config.Current;
        return cfg != null && cfg.DebugBuffer != DebugBuffer.Off;
    }

    internal static void Add(List<Job> jobs, string path, MyShaderProfile profile, string descriptor,
        params ShaderMacro[] macros)
    {
        if (jobs == null || string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        Enqueue(jobs, path, profile, descriptor, macros);
    }

    /// <summary>
    /// Keen Recompile fill: the compiler still remaps overlays. Do not require
    /// <see cref="File.Exists"/> — missing files fail the same way as vanilla.
    /// </summary>
    internal static void Enqueue(List<Job> jobs, string path, MyShaderProfile profile, string descriptor,
        ShaderMacro[] macros)
    {
        if (jobs == null || string.IsNullOrEmpty(path))
            return;
        jobs.Add(new Job(path, macros, profile, descriptor));
    }
}
