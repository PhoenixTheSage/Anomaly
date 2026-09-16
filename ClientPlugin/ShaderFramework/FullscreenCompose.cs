namespace ClientPlugin.Shaders;

/// <summary>
/// How Anomaly merges a data-driven fullscreen program into the slot target.
/// Resolve by name: <c>ClientPlugin.Shaders.FullscreenCompose</c>.
/// IsolatedAdd is the Overlay/Inject "inject" analog; Replace is exclusive.
/// </summary>
public enum FullscreenCompose
{
    IsolatedAdd = 0,
    IsolatedMix = 1,
    Chain = 2,
    PublishOnly = 3,
    Replace = 4,
    DirectAdd = 5,
    /// <summary>
    /// Scratch then blend occupancy onto dest: <c>dest.rgb * (1 - saturate(src.rgb))</c>.
    /// Pack writes a 0–1 dest fraction (<c>AnomalyIsolatedSub</c>). IsolatedSub
    /// draws the pack to scratch, stamps Reactive from <c>.a</c>, then merges
    /// with dest*(1-src) blend onto dest (same dest RTV as Replace). IsolatedSub
    /// does not blit dest for t0 — the pack does not composite dest.
    /// </summary>
    IsolatedSub = 6
}
