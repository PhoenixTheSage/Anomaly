namespace ClientPlugin.Shaders;

/// <summary>
/// Named insertion points Anomaly Harmony-owns. Packs register a draw
/// callback; they do not patch these Keen methods themselves.
/// Resolve by name: <c>ClientPlugin.Shaders.OwnedPassSlot</c>.
/// </summary>
public enum OwnedPassSlot
{
    /// <summary>Prefix <c>MyTransparentRendering.Render</c> — lighting done, atmosphere not yet.</summary>
    AfterLighting = 0,

    /// <summary>Postfix <c>MyAtmosphereRenderer.RenderGBuffer</c> — after Keen atmosphere and its per-planet clouds.</summary>
    AfterAtmosphere = 1,

    /// <summary>Postfix <c>MyTransparentRendering.Render</c> — OIT and additive-top billboards done.</summary>
    AfterTransparent = 2,

    /// <summary>Prefix <c>MyToneMapping.Run</c> — HDR <c>LBuffer</c> still live (internal res).</summary>
    BeforeTonemap = 3,

    /// <summary>Postfix <c>MyToneMapping.Run</c>, high priority — internal LDR, before SE-DLSS evaluate.</summary>
    AfterTonemap = 4,

    /// <summary>
    /// After an upscale consumer calls <see cref="OwnedPassRegistry.NotifyUpscaleComplete"/>.
    /// Output resolution. If nobody notifies, Anomaly runs this at <c>DrawGameScene</c> postfix (native res).
    /// </summary>
    AfterUpscale = 5
}
