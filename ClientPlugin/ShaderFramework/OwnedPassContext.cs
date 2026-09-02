using ClientPlugin.ShaderFramework;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Per-invocation state for an owned-pass draw. Resolve by name:
/// <c>ClientPlugin.Shaders.OwnedPassContext</c>. <see cref="Rc"/> is the
/// context the slot is recording on (may be deferred). Unbind before return.
/// </summary>
public sealed class OwnedPassContext
{
    internal OwnedPassContext(OwnedPassSlot slot, MyRenderContext rc, TemporalPolicy policy,
        bool outputResolution)
    {
        Slot = slot;
        Rc = rc;
        Policy = policy;
        IsOutputResolution = outputResolution;
        var size = outputResolution
            ? MyRender11.ViewportResolution
            : MyRender11.ResolutionI;
        Width = size.X;
        Height = size.Y;
    }

    public OwnedPassSlot Slot { get; }

    public MyRenderContext Rc { get; }

    public TemporalPolicy Policy { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>True for <see cref="OwnedPassSlot.AfterUpscale"/> after a consumer notified.</summary>
    public bool IsOutputResolution { get; }

    /// <summary>Keen HDR light buffer, or null when not in an LBuffer slot.</summary>
    public object LBuffer => MyGBuffer.Main?.LBuffer;

    /// <summary>Anomaly <c>reactiveMask</c> RTV (R8), or null if the pass did not request <see cref="TemporalPolicy.Reactive"/>.</summary>
    public object ReactiveTarget =>
        (Policy & TemporalPolicy.Reactive) != 0
            ? TemporalParticipation.ReactiveRtv
            : null;

    /// <summary>Catalog <c>linearDepth</c> SRV boxed as Keen <c>ISrvBindable</c>, or null.</summary>
    public object LinearDepthSrv => TemporalParticipation.LinearDepthSrv;

    /// <summary>
    /// Composite <paramref name="overlaySrv"/> into the published velocity
    /// buffer where <paramref name="maskSrv"/> is &gt; 0.5. Both are Keen
    /// <c>ISrvBindable</c>. No-op if the pass omitted
    /// <see cref="TemporalPolicy.ContributeVelocity"/>.
    /// </summary>
    public void ContributeVelocity(object overlaySrv, object maskSrv)
    {
        if ((Policy & TemporalPolicy.ContributeVelocity) == 0)
            return;
        TemporalParticipation.ContributeVelocity(Rc, overlaySrv as ISrvBindable, maskSrv as ISrvBindable);
    }
}
