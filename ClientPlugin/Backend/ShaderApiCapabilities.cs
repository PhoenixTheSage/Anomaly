using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

/// <summary>Implementation support, independent of registration or current-frame production.</summary>
public sealed class ShaderApiCapabilities
{
    public string Renderer {get;private set;}
    public bool RendererSelected {get;private set;}
    public bool PackRegistrationAvailable=>true;
    public bool CapturedSourcesAvailable=>true;
    public bool FullscreenExecutionSupported {get;private set;}
    public bool ClassicNullResourcesSupported {get;private set;}
    public bool PortablePreparationSupported {get;private set;}
    public bool PortableCommandsSupported {get;private set;}
    public bool NamedStageExecutionSupported {get;private set;}
    public bool SameDrawAttachmentsSupported {get;private set;}
    public string NamedStageStatus {get;private set;}
    internal static ShaderApiCapabilities Query()
    {
        bool dx=RenderHost.IsDx11&&Dx11Call.Ready,vulkan=RenderHost.IsVulkan;
        bool classicNulls=dx||vulkan&&GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.ClassicNullResources)&&NativeStageBindingStatus.Query().NullResourcesSupported;
        return new ShaderApiCapabilities
        {
            Renderer=RenderHost.IsDx11?"DX11":vulkan?"Vulkan":"pending",RendererSelected=RenderHost.Decided,
            FullscreenExecutionSupported=dx||vulkan&&GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.LiveResources|GuestBridgeClient.SharedFrame|8192|16384|32768|65536|GuestBridgeClient.ClassicNullResources)&&classicNulls,
            ClassicNullResourcesSupported=classicNulls,
            PortablePreparationSupported=dx&&Dx11Call.Supports("ClientPlugin.Shaders.OwnedPassRegistry","Register",6)||vulkan&&GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.LiveResources),
            PortableCommandsSupported=dx&&Dx11Call.Supports("ClientPlugin.Shaders.FullscreenPassRegistry","ExecutePortable",2)||vulkan&&GuestBridgeClient.SupportsPacketFlags(131072),
            NamedStageExecutionSupported=dx,SameDrawAttachmentsSupported=dx,
            NamedStageStatus=dx?"supported; support does not imply current-frame production":vulkan?"partial owned-raster, native postprocess, deferred lighting, atmosphere, foliage, decal, probe-processing, particle and screen-shadow owner generations available through stage state queries; query native binding status for descriptor records; other native owners and same-draw attachments remain unavailable":"waiting for renderer selection"
        };
    }
}
