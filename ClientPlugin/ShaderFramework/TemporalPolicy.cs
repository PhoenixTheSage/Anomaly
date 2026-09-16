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

    /// <summary>
    /// IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly reconstruct camera
    /// MVs from isolated.a (view-space hit distance, meters) and composite
    /// them over catalog velocity. C# owned passes may still call
    /// <see cref="OwnedPassContext.ContributeVelocity"/>.
    /// </summary>
    ContributeVelocity = 2,

    /// <summary>
    /// IsolatedAdd / IsolatedMix / IsolatedSub / DirectAdd / PublishOnly with this flag
    /// stamp dilated isolated luma into <c>reactiveMask</c>. IsolatedSub umbra
    /// must use this (not ContributeVelocity) so DLSS / FRS reject history.
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
