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
        SceneView.Width = 95; SceneView.Height = 73;
        var half = new GuestProgram { Id = "parity.half", Mark = 1, Compose = "PublishOnly", Pipeline = 31, Scale = .5f };
        GuestBridgeClient.Submit(new[] { half });
        Require(half.ImageWidth == 47 && half.ImageHeight == 36, "DX11 half-resolution floor contract failed");
        foreach (string mode in new[] { "IsolatedAdd", "IsolatedMix", "IsolatedSub", "Chain", "Replace", "PublishOnly" })
        {
            var pass = new GuestProgram { Id = "parity." + mode, Mark = 2, Compose = mode, Pipeline = 32, HistoryName = "parity.history" };
            GuestBridgeClient.Submit(new[] { pass });
            Require(pass.HistoryImage != 0 && pass.HistoryReady, mode + " did not capture isolated output history");
        }
        var consumer = new GuestProgram { Id = "parity.early", Mark = 1, Compose = "PublishOnly", Pipeline = 41,
            Srvs = new List<SrvBind> { new SrvBind { Slot = 7, Name = "parity.lateHistory" } } };
        var producer = new GuestProgram { Id = "parity.late", Mark = 1, Compose = "PublishOnly", Pipeline = 42, HistoryName = "parity.lateHistory" };
        var programs = new[] { consumer, producer };
        for (int frame = 0; frame < 4; frame++)
        {
            ulong expected = producer.HistoryReady ? producer.HistoryImage : 0;
            GuestBridgeClient.Submit(programs);
            Require(GuestBridge.Packets.First(p => p.Pipeline == consumer.Pipeline).Sampled[7] == expected,
                "Early consumer did not sample exactly the previous frame at frame " + frame);
        }
        Console.WriteLine("PASS: parity packet contracts: odd size, all compose histories, previous-frame aliases");
    }
}
