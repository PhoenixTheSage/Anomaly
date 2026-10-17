using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Backend;

static partial class NativeProbe
{
    public static void DrawOccupancyProbe(ulong splat, ulong stamp, ulong diagnostic)
    {
        const int width = 9, height = 3, size = 512;
        const ulong depth = 920, atlas = 921, boxes = 922;
        if ((Fn<FeaturesFn>("sevk_guest_features")() & 48) != 48) throw new Exception("native occupancy capabilities missing");
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, depth, width, height, 1, 2));
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, atlas, size, size, 1, 2));
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, boxes, 65 * 32, 1, 1, 4));
        var frame = new byte[344];
        void F(int o, float v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, frame, o, 4);
        F(60, 1.5f); F(64, 1.1f); F(24, .1f); F(28, -.05f);
        // Ninety-degree yaw. Camera translation is deliberately huge and must
        // not enter this camera-relative clipmap's rotation-only splat.
        F(272, -1); F(284, 1); F(296, 1); F(312, 1e12f); F(316, -1e12f); F(320, 1e12f); F(324, 1);
        var samples = new float[width * height];
        for (int i = 0; i < samples.Length; i++) samples[i] = i % 7 == 0 ? 0 : 3 + i;
        var bounds = new float[65 * 8];
        for (int i = 0; i < 65; i++)
        {
            bounds[i*8] = i == 64 ? 22 : 5; bounds[i*8+1] = i == 64 ? -14 : 5; bounds[i*8+2] = -8;
            bounds[i*8+4] = .8f; bounds[i*8+5] = 1.1f; bounds[i*8+6] = .9f;
        }
        void Upload(ulong id, float[] data)
        {
            var raw = new byte[data.Length * 4]; Buffer.BlockCopy(data, 0, raw, 0, raw.Length);
            var pin = GCHandle.Alloc(raw, GCHandleType.Pinned);
            try { Check(Fn<UpdateFn>("sevk_guest_update_image")(context, id, pin.AddrOfPinnedObject(), (ulong)raw.Length)); }
            finally { pin.Free(); }
        }
        int Pixel(int x, int y, int z) => ((z / 8) * 64 + y) * size + (z % 8) * 64 + x;
        void Run(bool stampBoxes, string label)
        {
            var expected = new HashSet<int>();
            void Cell(float x, float y, float z)
            {
                int cx = (int)Math.Floor((x + 64) / 2), cy = (int)Math.Floor((y + 64) / 2), cz = (int)Math.Floor((z + 64) / 2);
                if (cx >= 0 && cy >= 0 && cz >= 0 && cx < 64 && cy < 64 && cz < 64) expected.Add(Pixel(cx, cy, cz));
            }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float d = samples[y * width + x]; if (d < 1e-4) continue;
                float rx = .1f / 1.5f + (-1f + 2f * ((x + .5f) / width)) / 1.5f;
                float ry = -.05f / 1.1f - (-1f + 2f * ((y + .5f) / height)) / 1.1f;
                // Expanded rotation rows: world = row0*view.x + row1*view.y + row2*view.z.
                Cell(-d, d * ry, -d * rx);
            }
            if (stampBoxes)
                for (int box = 0; box < 65; box++)
                {
                    int[] first = new int[3], last = new int[3];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        first[axis] = Math.Clamp((int)Math.Floor((bounds[box*8+axis] - bounds[box*8+4+axis] + 64) / 2), 0, 63);
                        last[axis] = Math.Clamp((int)Math.Floor((bounds[box*8+axis] + bounds[box*8+4+axis] + 64) / 2), 0, 63);
                    }
                    for (int z = first[2]; z <= last[2]; z++) for (int y = first[1]; y <= last[1]; y++) for (int x = first[0]; x <= last[0]; x++) expected.Add(Pixel(x,y,z));
                }
            Upload(depth, samples); if (stampBoxes) Upload(boxes, bounds);
            Check(Fn<ResetFn>("sevk_guest_reset")(context));
            var packets = new byte[stampBoxes ? 832 : 416];
            void U32(int o, uint v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, packets, o, 4);
            void U64(int o, ulong v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, packets, o, 8);
            byte[] constants = GuestOccupancy.Constants(frame, width, height, stampBoxes ? 65 : 0);
            for (int start = 0; start < packets.Length; start += 416)
            {
                bool isStamp = start != 0;
                U32(start, 416); U32(start+4, 1); U32(start+8, 1); U32(start+12, 5);
                U32(start+16, isStamp ? 65u : width); U32(start+20, isStamp ? 1u : height);
                U64(start+24, isStamp ? stamp : splat); U64(start+32, atlas); U32(start+40, 256); U32(start+44, isStamp ? 34u : 50u);
                Buffer.BlockCopy(constants, 0, packets, start+48, 256); U64(start+304, isStamp ? boxes : depth);
            }
            var pin = GCHandle.Alloc(packets, GCHandleType.Pinned);
            try { Check(Fn<SubmitFn>("sevk_guest_submit")(context, pin.AddrOfPinnedObject(), (uint)(packets.Length/416))); } finally { pin.Free(); }
            Check(Fn<ReplayFn>("sevk_guest_replay_now")(context, 1));
            var result = new byte[size * size * 4]; pin = GCHandle.Alloc(result, GCHandleType.Pinned);
            try { Check(Fn<ReadFn>("sevk_guest_readback")(context, atlas, pin.AddrOfPinnedObject(), (ulong)result.Length)); } finally { pin.Free(); }
            int missing = 0, extra = 0, firstMismatch = -1;
            for (int i = 0; i < size * size; i++)
            {
                float value = BitConverter.ToSingle(result, i*4);
                if (value != (expected.Contains(i) ? 1 : 0))
                { if (firstMismatch < 0) firstMismatch = i; if (expected.Contains(i)) missing++; else extra++; }
            }
            if (firstMismatch >= 0) throw new Exception("occupancy " + label + " first mismatch=" + firstMismatch + " missing=" + missing + " extra=" + extra + " expected=" + expected.Count);
        }
        Upload(boxes, bounds);
        var readPacket = new byte[416];
        void Write32(int o, uint v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, readPacket, o, 4);
        void Write64(int o, ulong v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, readPacket, o, 8);
        Write32(0, 416); Write32(4, 1); Write32(8, 1); Write32(12, 5); Write32(16, 1); Write32(20, 1);
        Write64(24, diagnostic); Write64(32, atlas); Write32(40, 256); Write32(44, 50); Write64(304, boxes);
        Buffer.BlockCopy(GuestOccupancy.Constants(frame, width, height, 65), 0, readPacket, 48, 256);
        Check(Fn<ResetFn>("sevk_guest_reset")(context));
        var pinned = GCHandle.Alloc(readPacket, GCHandleType.Pinned);
        try { Check(Fn<SubmitFn>("sevk_guest_submit")(context, pinned.AddrOfPinnedObject(), 1)); } finally { pinned.Free(); }
        Check(Fn<ReplayFn>("sevk_guest_replay_now")(context, 1));
        var readResult = new byte[size*size*4]; pinned = GCHandle.Alloc(readResult, GCHandleType.Pinned);
        try { Check(Fn<ReadFn>("sevk_guest_readback")(context, atlas, pinned.AddrOfPinnedObject(), (ulong)readResult.Length)); } finally { pinned.Free(); }
        float[] expectedBindings = {65, 64, 5, .8f, 22};
        for (int i = 0; i < expectedBindings.Length; i++)
            if (BitConverter.ToSingle(readResult, i*4) != expectedBindings[i]) throw new Exception("occupancy constants/structured input mismatch at " + i);
        Run(false, "rotated depth");
        Run(true, "depth and 65 AABBs");
        for (int i = 0; i < bounds.Length; i += 8) bounds[i] += 4;
        Run(true, "updated box buffer");
        Array.Clear(samples); Run(false, "next-frame clear");
        Console.WriteLine("GPU PASS: entire occupancy atlas for rotated/jittered depth, partial workgroups, 65 box stamps and next-frame clearing");
    }
}
