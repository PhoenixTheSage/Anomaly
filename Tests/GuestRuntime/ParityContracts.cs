using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    // Production packet construction, with only the native transport replaced.
    // These regressions were first reproduced during the 2026-10-21 parity review.
    static void ParityContracts()
    {
        GuestBridge.Ready = true;
        void Submit(GuestProgram[] passes)
        {
            var list=new List<GuestProgram>(passes);
            for(int mode=0;mode<4;mode++)list.Add(new GuestProgram { Id="anomaly.helper.Merge"+mode,Helper=true,Pipeline=(ulong)(91+mode),Mark=1 });
            GuestBridgeClient.Submit(list);
        }
        SceneView.Width = 95; SceneView.Height = 73;
        var half = new GuestProgram { Id = "parity.half", Mark = 1, Compose = "PublishOnly", Pipeline = 31, Scale = .5f };
        Submit(new[] { half });
        Require(half.ImageWidth == 47 && half.ImageHeight == 36, "DX11 half-resolution floor contract failed");
        foreach (string mode in new[] { "IsolatedAdd", "IsolatedMix", "IsolatedSub", "Chain", "Replace", "PublishOnly" })
        {
            var pass = new GuestProgram { Id = "parity." + mode, Mark = 2, Compose = mode, Pipeline = 32, HistoryName = "parity.history" };
            Submit(new[] { pass });
            Require(pass.HistoryImage != 0 && pass.HistoryReady, mode + " did not capture isolated output history");
        }
        var consumer = new GuestProgram { Id = "parity.early", Mark = 1, Compose = "PublishOnly", Pipeline = 41,
            Srvs = new List<SrvBind> { new SrvBind { Slot = 7, Name = "parity.lateHistory" } } };
        var producer = new GuestProgram { Id = "parity.late", Mark = 1, Compose = "PublishOnly", Pipeline = 42, HistoryName = "parity.lateHistory" };
        var programs = new[] { consumer, producer };
        for (int frame = 0; frame < 4; frame++)
        {
            ulong expected = producer.HistoryReady ? producer.HistoryImage : 0;
            Submit(programs);
            Require(GuestBridge.Packets.First(p => p.Pipeline == consumer.Pipeline).Sampled[7] == expected,
                "Early consumer did not sample exactly the previous frame at frame " + frame);
        }
        producer.Enabled=false;Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active(producer.HistoryName)==null && !producer.HistoryReady,"disabled pass retained history");
        producer.Enabled=true;Submit(programs);
        Require(GuestBridge.Packets.First(p=>p.Pipeline==consumer.Pipeline).Sampled[7]==0,"re-enabled history skipped its first invalid frame");
        var first=new GuestProgram{Id="parity.chain1",Mark=1,Compose="Chain",Pipeline=51};
        var second=new GuestProgram{Id="parity.chain2",Mark=1,Compose="Chain",Pipeline=52};
        var other=new GuestProgram{Id="parity.chainOtherSlot",Mark=2,Compose="Chain",Pipeline=53};
        Submit(new[]{first,second,other});
        Require(GuestBridge.Packets.First(p=>p.Pipeline==52).Sampled[0]==first.StorageImage,"Chain did not consume preceding isolated product");
        Require(GuestBridge.Packets.First(p=>p.Pipeline==53).Sampled[0]==GuestBridgeClient.Hdr,"Chain state crossed slot boundary");
        Require(GuestBridge.Packets.Count(p=>p.Pipeline==91 && p.Storage==GuestBridgeClient.Hdr)==2,"final Chain did not present each slot");
        Console.WriteLine("PASS: parity packet contracts: odd size, all compose histories, previous-frame aliases");
    }
}
