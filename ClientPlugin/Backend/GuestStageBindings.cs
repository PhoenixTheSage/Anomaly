namespace ClientPlugin.Backend;

// The shared facade retains reservations even before renderer selection. Native
// owner descriptors remain a separate execution contract; storing a reservation
// must never advertise that a Vulkan shader consumed it.
internal static class GuestStageBindings
{
    internal static readonly ClientPlugin.Contracts.StageBindingModel Model = new();
    internal static string Status => "Vulkan native stage binding execution unavailable; reservations retained";

    internal static void BindDx11()
    {
        foreach (var request in Model.Snapshot())
            Dx11Call.Try("ClientPlugin.Shaders.ShaderBindRegistry", "RequestSrv",
                new object[] { request.Stage, request.CatalogName, request.Slot }, out _);
    }
}
