using System;
using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

/// <summary>Native descriptor records, independent of GPU execution/completion.
/// Compare FrameIndex with the native shader variant's production frame.</summary>
public sealed class NativeStageBindingStatus
{
    public bool TransportSupported {get;private set;}
    public bool NullResourcesSupported {get;private set;}
    public int RegisteredReservations {get;private set;}
    public uint FrameIndex {get;private set;}=uint.MaxValue;
    public uint QueuedOwners {get;private set;}
    public ulong CatalogDescriptorsResolved {get;private set;}
    public ulong CatalogDescriptorsMissing {get;private set;}
    public ulong CatalogDescriptorsMismatched {get;private set;}
    public string Error {get;private set;}
    internal static NativeStageBindingStatus Query()
    {
        var status=new NativeStageBindingStatus{TransportSupported=GuestBridgeClient.SupportsPacketFlags(GuestStageBindings.Feature),
            RegisteredReservations=GuestStageBindings.Model.Snapshot().Count};
        if(!status.TransportSupported){status.Error="Update SE-Vulkan: native stage bindings require bit22.";return status;}
        try
        {
            var data=GuestStageBindings.State();
            if(data==null||data.Length<6){status.Error="Native stage binding status unavailable before device creation.";return status;}
            status.NullResourcesSupported=((uint)data[0]&1)!=0;status.QueuedOwners=(uint)data[1];status.FrameIndex=(uint)data[2];
            status.CatalogDescriptorsResolved=(ulong)data[3];status.CatalogDescriptorsMissing=(ulong)data[4];status.CatalogDescriptorsMismatched=(ulong)data[5];
        }
        catch(Exception error){status.Error=error.GetBaseException().Message;}
        return status;
    }
}
