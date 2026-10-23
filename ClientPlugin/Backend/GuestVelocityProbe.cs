namespace ClientPlugin.Backend;

/// <summary>DX11's GBuffer velocity developer probes (Config.VelocityProbe) on Vulkan, applied between frames.</summary>
internal static class GuestVelocityProbe
{
    static uint applied;
    static long appliedEpoch=-1;
    internal static void Apply()
    {
        uint mode=(uint)(Config.Current?.VelocityProbe??VelocityProbe.Off);
        long epoch=GuestBridgeClient.ResourceEpoch;
        if(mode==applied&&epoch==appliedEpoch)return;
        // Older hosts lack the call and keep stock motion.
        if(GuestBridgeClient.Call("SetGBufferVelocityProbe",mode) is true){applied=mode;appliedEpoch=epoch;}
    }
}
