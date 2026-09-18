using System;
using SharpDX.Direct3D11;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>Nonblocking GPU timestamps. A full query ring drops a measurement, never stalls rendering.</summary>
internal sealed class VolumetricGpuTimer : IDisposable
{
    readonly Sample[] ring = new Sample[4];
    int write;
    Sample active;
    internal double LastMilliseconds { get; private set; } = double.NaN;
    internal long Measurements { get; private set; }
    internal void Poll(DeviceContext context)
    {
        if (context.TypeInfo != DeviceContextType.Immediate) return;
        foreach(var sample in ring)
        {
            if(sample == null || !sample.Pending) continue;
            if(!context.GetData(sample.Disjoint,AsynchronousFlags.DoNotFlush,out QueryDataTimestampDisjoint period) ||
               !context.GetData(sample.End,AsynchronousFlags.DoNotFlush,out long end) ||
               !context.GetData(sample.Start,AsynchronousFlags.DoNotFlush,out long start)) continue;
            sample.Pending=false;
            if(period.Disjoint || period.Frequency <= 0 || end < start) continue;
            LastMilliseconds=(end-start)*1000.0/period.Frequency;
            Measurements++;
        }
    }
    internal bool Begin(DeviceContext context)
    {
        if(active != null) throw new InvalidOperationException("Volume GPU timer already active");
        Poll(context);
        var sample=ring[write] ?? (ring[write]=new Sample());
        if(sample.Pending) return false;
        write=(write+1)%ring.Length; active=sample;
        context.Begin(sample.Disjoint); context.End(sample.Start);
        return true;
    }
    internal void End(DeviceContext context)
    {
        if(active == null) return;
        context.End(active.End); context.End(active.Disjoint); active.Pending=true; active=null;
    }
    public void Dispose()
    {
        for(int i=0;i<ring.Length;i++) { ring[i]?.Dispose(); ring[i]=null; }
        active=null; LastMilliseconds=double.NaN; Measurements=0; write=0;
    }
    sealed class Sample : IDisposable
    {
        internal Query Start,End,Disjoint;
        internal bool Pending;
        internal Sample()
        {
            try {
                Start=new Query(MyRender11.DeviceInstance,new QueryDescription{Type=QueryType.Timestamp});
                End=new Query(MyRender11.DeviceInstance,new QueryDescription{Type=QueryType.Timestamp});
                Disjoint=new Query(MyRender11.DeviceInstance,new QueryDescription{Type=QueryType.TimestampDisjoint});
            } catch { Dispose(); throw; }
        }
        public void Dispose() { Start?.Dispose(); End?.Dispose(); Disjoint?.Dispose(); }
    }
}
