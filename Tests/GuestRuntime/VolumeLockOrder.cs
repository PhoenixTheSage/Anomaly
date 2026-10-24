using System;
using System.IO;
using System.Threading;
using ClientPlugin.Backend;
using ClientPlugin.Shaders;

static partial class Program
{
    // The live Vulkan main-menu hang: guest submission holds GuestPassList.Gate while pack callbacks run, and
    // Clouds publishes its medium from that callback, while FinalFrontier's AtmosphereBridge updates the same
    // registry from the game thread. The facade used to take GuestPassList.Gate inside its own gate (deadlock).
    static void VolumeLockOrder()
    {
        const string id = "fixture.lock.medium";
        string folder = Path.Combine(Path.GetTempPath(), "anomaly-volume-lock-order");
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "medium.hlsli");
        File.WriteAllText(source, "AnomalyMediumSample EvaluateMedium(float3 p) { return AnomalyEmptyMedium(); }\n");
        Require(!Dx11Call.Ready && !RendererRoute.UseDx11Device, "lock-order fixture runs on the shared (non-DX11) route");
        Require(VolumetricMediumRegistry.Register(id, source, Array.Empty<string>()), "fixture medium registration rejected");
        for (int round = 0; round < 20; round++)
        {
            using var inside = new ManualResetEventSlim();
            Exception renderError = null, gameError = null;
            // Render thread: submission holds GuestPassList.Gate, then the pack callback publishes to the registry.
            var render = new Thread(() =>
            {
                try { GuestPassList.SubmitFrame(_ => { inside.Set(); Thread.Sleep(50); VolumetricMediumRegistry.SetEnabled(id, true); }); }
                catch (Exception e) { renderError = e; }
            });
            // Game thread: a registry update while that submission is in flight.
            var game = new Thread(() =>
            {
                try { inside.Wait(); VolumetricMediumRegistry.SetEnabled(id, false); VolumetricMediumRegistry.SetOrigin(id, 1, 2, 3); }
                catch (Exception e) { gameError = e; }
            });
            render.IsBackground = game.IsBackground = true;
            render.Start(); game.Start();
            bool finished = render.Join(10000) & game.Join(10000);
            Require(finished, "registry update during guest submission deadlocked (round " + round + ")");
            Require(renderError == null && gameError == null, "lock-order fixture failed: " + (renderError ?? gameError)?.Message);
            bool enabled = VolumeRegistryState.IsEnabled(id), guestEnabled = false;
            foreach (var program in GuestPassList.Snapshot()) if (program.Id == id) guestEnabled = program.Enabled;
            Require(enabled == guestEnabled, "guest pass enable state diverged from the registry after concurrent updates");
        }
        VolumetricMediumRegistry.Unregister(id);
        Console.WriteLine("PASS: volume registry updates during guest submission complete without deadlock and converge (20 rounds)");
    }
}
