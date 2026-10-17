using System;
using System.Collections.Generic;
using ClientPlugin.Buffers;
namespace ClientPlugin.Backend;

// CPU registries survive native context replacement. Every device-owned cache
// must discard handles together, before any cached pipeline can be submitted.
internal static class GuestDevice
{
    static long epoch=-1;
    static bool pending,notifyEnd;
    internal static bool Synchronize()
    {
        long current=GuestBridgeClient.ResourceEpoch;
        if(current!=epoch){notifyEnd|=epoch>0;epoch=current;pending=true;}
        if(!pending)return true;
        var errors=new List<Exception>();
        try{VolumeRegistryState.ResetIntervals();}catch(Exception error){errors.Add(error);}
        try{BufferCatalog.ResetGuestDevice(notifyEnd);notifyEnd=false;}catch(Exception error){errors.Add(error);}
        foreach(var p in GuestPassList.Snapshot()) {
            p.Pipeline=0;p.StorageImage=p.HistoryImage=0;p.ImageWidth=p.ImageHeight=0;p.HistoryReady=false;p.SubmittedThisFrame=false;
            if(p.Status!="compile-failed"){p.Status="renderer-pending";p.Error=null;}
        }
        foreach(var p in GuestPassList.SnapshotAttachments())if(p.Status!="compile-failed")p.Status="renderer-pending";
        GuestFrame.ResetDevice();GuestBridgeClient.ResetDevice();GuestOccupancy.ResetDevice();GuestPointShadows.ResetDevice();GuestVolumes.ResetDevice();GuestColor.ResetDevice();
        if(errors.Count!=0){VolumeRegistryState.SetStatus("Guest device reset incomplete: "+new AggregateException(errors).Message);return false;}
        try {
            if(current!=0){BufferCatalog.ApplyDeferred();GuestColor.RestoreDevice();}
            pending=false;return true;
        }catch(Exception error){VolumeRegistryState.SetStatus("Guest device restore pending: "+error.GetBaseException().Message);return false;}
    }
}
