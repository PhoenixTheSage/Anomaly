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

    /// <summary>The pass may write <c>reactiveMask</c> (cleared to 0 at frame start). High = do not trust history.</summary>
    Reactive = 4
}
