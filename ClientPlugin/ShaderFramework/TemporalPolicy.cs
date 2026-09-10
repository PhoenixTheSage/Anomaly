using System;

namespace ClientPlugin.Shaders;

/// <summary>
/// How an owned pass participates in color / motion / DLSS.
/// Resolve by name: <c>ClientPlugin.Shaders.TemporalPolicy</c>.
/// </summary>
[Flags]
public enum TemporalPolicy
{
    None = 0,

    /// <summary>Writes into HDR <c>LBuffer</c> (or LDR at AfterTonemap). Temporal consumers will see the color.</summary>
    InColor = 1,

    /// <summary>After draw, the pass may call <see cref="OwnedPassContext.ContributeVelocity"/> to composite extra MVs.</summary>
    ContributeVelocity = 2,

    /// <summary>
    /// IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly with this flag
    /// stamp dilated isolated luma into <c>reactiveMask</c>. C# owned passes
    /// may still write the RTV. High = do not trust history.
    /// </summary>
    Reactive = 4,

    /// <summary>
    /// AfterUpscale display-referred grade (BT.2390 / scRGB / paper-white).
    /// The unique upscale consumer should evaluate pre-tonemap HDR, skip Keen
    /// SDR tonemap, and call <see cref="OwnedPassRegistry.NotifyUpscaleComplete"/>
    /// with the dest so this pass reads <c>upscaledColor</c> — not raw
    /// <c>LBuffer</c>.
    /// </summary>
    Display = 8
}
