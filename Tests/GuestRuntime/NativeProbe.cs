using System;
using System.IO;
using System.Runtime.InteropServices;

// Optional strict GPU lane: every production-compiled module must be accepted
// by the real guest reflection/pipeline factory. Missing hardware is a failure.
static partial class NativeProbe
{
    static IntPtr module, context;
    public static bool Enabled => context != IntPtr.Zero;
    public static IntPtr Context => context;
    public static IntPtr Symbol(string name) => NativeLibrary.GetExport(module,name);
    public static bool Supports(int mask) => !Enabled || (Fn<FeaturesFn>("sevk_guest_features")() & mask) == mask;
    public static void ImageCreate(ulong id, int width, int height, int depth, int format) => Check(Fn<ImageFn>("sevk_guest_create_image")(context, id, (uint)width, (uint)height, (uint)depth, (uint)format));
    public static void ImageUpdate(ulong id, byte[] bytes)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Check(Fn<UpdateFn>("sevk_guest_update_image")(context, id, pin.AddrOfPinnedObject(), (ulong)bytes.Length)); } finally { pin.Free(); }
    }
    public static void ResetQueue() => Check(Fn<ResetFn>("sevk_guest_reset")(context));
    public static void Replay(int mark) => Check(Fn<ReplayFn>("sevk_guest_replay_now")(context, (uint)mark));
    public static byte[] ReadRaw(ulong id, int bytes)
    {
        var output = new byte[bytes]; var pin = GCHandle.Alloc(output, GCHandleType.Pinned);
        try { Check(Fn<ReadFn>("sevk_guest_readback")(context, id, pin.AddrOfPinnedObject(), (ulong)bytes)); } finally { pin.Free(); }
        return output;
    }
    public static void Queue(int mark, int compose, int width, int height, ulong pipeline, ulong storage, byte[] uniforms, ulong[] sampled, int flags)
    {
        var packet = new byte[416];
        void U(int at, uint value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, packet, at, 4);
        void L(int at, ulong value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, packet, at, 8);
        U(0,416); U(4,1); U(8,(uint)mark); U(12,(uint)compose); U(16,(uint)width); U(20,(uint)height); L(24,pipeline); L(32,storage);
        U(40,(uint)uniforms.Length); U(44,(uint)flags); Buffer.BlockCopy(uniforms,0,packet,48,uniforms.Length);
        for (int i=0;i<sampled.Length;i++) L(304+i*8,sampled[i]);
        var pin=GCHandle.Alloc(packet,GCHandleType.Pinned);
        try { Check(Fn<SubmitFn>("sevk_guest_submit")(context,pin.AddrOfPinnedObject(),1)); } finally { pin.Free(); }
    }
    static T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module, name));
    internal static void Check(int rc) { if (rc != 0) throw new Exception("native guest: " + Marshal.PtrToStringAnsi(Fn<ErrorFn>("sevk_last_error")(context))); }
    public static void Open(string root)
    {
        string rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        string built = Path.Combine(root, "Native", "build", "Release", "se_vulkan_native.dll");
        string isolated=Environment.GetEnvironmentVariable("ANOMALY_TEST_NATIVE_DLL");
        if(!string.IsNullOrWhiteSpace(isolated))
        {
            built=Path.GetFullPath(isolated);
            if(!File.Exists(built))throw new FileNotFoundException("Requested isolated native test library is missing.",built);
            Console.WriteLine("GPU native: "+built+" SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(built))));
        }
        module = NativeLibrary.Load(OperatingSystem.IsWindows() && File.Exists(built) ? built
            : Path.Combine(root, "Assets", "Native", rid, OperatingSystem.IsWindows() ? "se_vulkan_native.dll" : "libse_vulkan_native.so"));
        var shaders = Marshal.StringToHGlobalAnsi(Path.Combine(root, "Assets", "Shaders", "Compiled"));
        var cache = Marshal.StringToHGlobalAnsi(Path.Combine(Path.GetTempPath(), "anomaly-guest-gpu"));
        try
        {
            var info = new Info { size = (uint)Marshal.SizeOf<Info>(), abi = 1, validation = 1, width = 8, height = 8, shaders = shaders, cache = cache };
            Check(Fn<CreateFn>("sevk_create")(ref info, out context));
        }
        finally { Marshal.FreeHGlobal(shaders); Marshal.FreeHGlobal(cache); }
        Console.WriteLine("GPU: real Vulkan guest pipeline factory enabled");
        if ((Fn<FeaturesFn>("sevk_guest_features")() & 8) == 0) throw new Exception("native lit-mip capability missing");
    }
    public static ulong Pipeline(byte[] vs, byte[] ps, string ve, string pe)
    {
        var v = GCHandle.Alloc(vs, GCHandleType.Pinned); var p = GCHandle.Alloc(ps, GCHandleType.Pinned);
        try
        {
            Check(Fn<PipelineFn>("sevk_guest_create_pipeline")(context, v.AddrOfPinnedObject(), (ulong)vs.Length, ve, p.AddrOfPinnedObject(), (ulong)ps.Length, pe, out ulong id));
            if (id == 0) throw new Exception("native returned no pipeline");
            return id;
        }
        finally { v.Free(); p.Free(); }
    }
    public static void Close()
    {
        if (context != IntPtr.Zero)
        {
            var stats = new Stats { size = (uint)Marshal.SizeOf<Stats>(), abi = 1 };
            Check(Fn<StatsFn>("sevk_get_stats")(context, ref stats));
            Fn<DestroyFn>("sevk_destroy")(context);
            if (stats.validationErrors != 0) throw new Exception("GPU validation errors: " + stats.validationErrors);
            Console.WriteLine("GPU: " + stats.device + "; validation enabled=" + stats.validation + "; errors=" + stats.validationErrors);
        }
        context = IntPtr.Zero;
    }
    public static void DrawFrameProbe(ulong pipeline)
    {
        if (!Enabled) return;
        if (BitConverter.ToUInt64(Execution(pipeline),16) != 0) throw new Exception("Pipeline creation incorrectly reported execution");
        const ulong image = 900;
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, image, 8, 8, 1, 0));
        var packet = new byte[416];
        void U32(int offset, uint value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, packet, offset, 4);
        void U64(int offset, ulong value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, packet, offset, 8);
        U32(0, 416); U32(4, 1); U32(8, 1); U32(12, 5); U32(16, 8); U32(20, 8); U64(24, pipeline); U64(32, image);
        var pin = GCHandle.Alloc(packet, GCHandleType.Pinned);
        try { Check(Fn<SubmitFn>("sevk_guest_submit")(context, pin.AddrOfPinnedObject(), 1)); }
        finally { pin.Free(); }
        Check(Fn<ReplayFn>("sevk_guest_replay_now")(context, 1));
        var executed = Execution(pipeline);
        if (BitConverter.ToUInt64(executed,16) != 1 || BitConverter.ToUInt64(executed,24) > BitConverter.ToUInt64(executed,32))
            throw new Exception("Recorded/completed guest execution status diverged");
        var rgba = new byte[8 * 8 * 4]; pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try { Check(Fn<ReadFn>("sevk_guest_readback")(context, image, pin.AddrOfPinnedObject(), (ulong)rgba.Length)); }
        finally { pin.Free(); }
        if (Math.Abs(rgba[0] - 16) > 2 || Math.Abs(rgba[1] - 16) > 2 || Math.Abs(rgba[252] - 239) > 2 || Math.Abs(rgba[253] - 239) > 2)
            throw new Exception("stock frame/UV GPU pixels diverged: " + rgba[0] + "," + rgba[1] + " / " + rgba[252] + "," + rgba[253]);
        Console.WriteLine("GPU PASS: 1280-byte stock frame draw, uncorrupted matrix bytes, fullscreen UV orientation");
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int BeginFrameFn(IntPtr context,IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EndFrameFn(IntPtr context);
    public static void LiveGuestResources(byte[] vs,byte[] ps,string entry,ClientPlugin.Backend.GuestProgram volume)
    {
        if(!Supports(1024)) throw new Exception("native guest live resource phase missing");
        var frame=new byte[144];
        for(int i=0;i<4;i++) Buffer.BlockCopy(BitConverter.GetBytes(1f),0,frame,i*20,4);
        var pin=GCHandle.Alloc(frame,GCHandleType.Pinned);
        ResetQueue();
        try { Check(Fn<BeginFrameFn>("sevk_begin_frame")(context,pin.AddrOfPinnedObject())); } finally { pin.Free(); }
        // Reproduce the actual host timing: factories and image replacement
        // happen in its callback while the frame is open, before EndFrame.
        ulong pipeline=Pipeline(vs,ps,"main",entry);
        Pipeline2(volume.VertexSpirv,volume.PixelSpirv,"compute",volume.PixelEntry,1);
        const ulong output=995;
        ImageCreate(output,4,4,1,0); ImageUpdate(output,new byte[4*4*4]);
        ImageCreate(output,8,8,1,0); ImageUpdate(output,new byte[8*8*4]);
        Queue(1,5,8,8,pipeline,output,Array.Empty<byte>(),new ulong[14],0);
        Check(Fn<EndFrameFn>("sevk_end_frame")(context));
        // This fixture opens a menu frame, not a queued world. Replay afterward
        // proves the callback-created pipeline/image remain usable. Frame
        // packets are consumed at EndFrame, so explicit replay needs a new packet.
        Queue(1,5,8,8,pipeline,output,Array.Empty<byte>(),new ulong[14],0);
        Replay(1);
        var pixels=ReadRaw(output,8*8*4);
        if(Math.Abs(pixels[0]-16)>2 || Math.Abs(pixels[1]-16)>2 || Math.Abs(pixels[252]-239)>2 || Math.Abs(pixels[253]-239)>2)
            throw new Exception("live guest resource phase corrupted shader/image pixels");
        ResetQueue();
        Console.WriteLine("GPU PASS: real BeginFrame/EndFrame guest pipeline factories, image upload/replacement and subsequent pixel replay");
    }
    public static void DrawMipProbe(ulong pipeline)
    {
        const ulong source = 910, pyramid = 911, output = 912;
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, source, 8, 8, 1, 1));
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, pyramid, 8, 8, 1, (4u << 16) | 1));
        Check(Fn<ImageFn>("sevk_guest_create_image")(context, output, 8, 8, 1, 0));
        void Upload(float even, float odd)
        {
            var pixels = new byte[8 * 8 * 8];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) for (int c = 0; c < 4; c++)
                Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)(c == 3 ? 1 : ((x + y) % 2 == 0 ? even : odd)))), 0, pixels, (y * 8 + x) * 8 + c * 2, 2);
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { Check(Fn<UpdateFn>("sevk_guest_update_image")(context, source, pin.AddrOfPinnedObject(), (ulong)pixels.Length)); }
            finally { pin.Free(); }
        }
        void Draw(float lod, byte expected, byte? oddExpected = null)
        {
            Check(Fn<ResetFn>("sevk_guest_reset")(context));
            var packets = new byte[2 * 416];
            void U32(int o, uint v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, packets, o, 4);
            void U64(int o, ulong v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, packets, o, 8);
            for (int start = 0; start < packets.Length; start += 416)
            { U32(start, 416); U32(start + 4, 1); U32(start + 8, 1); U32(start + 12, 5); U32(start + 16, 8); U32(start + 20, 8); U64(start + 24, pipeline); }
            U64(32, pyramid); U32(44, 8); U64(304, source);
            U64(416 + 32, output); U64(416 + 304, pyramid); U32(416 + 40, 16);
            Buffer.BlockCopy(BitConverter.GetBytes(lod), 0, packets, 416 + 48, 4);
            var pin = GCHandle.Alloc(packets, GCHandleType.Pinned);
            try { Check(Fn<SubmitFn>("sevk_guest_submit")(context, pin.AddrOfPinnedObject(), 2)); } finally { pin.Free(); }
            Check(Fn<ReplayFn>("sevk_guest_replay_now")(context, 1));
            var result = new byte[8 * 8 * 4]; pin = GCHandle.Alloc(result, GCHandleType.Pinned);
            try { Check(Fn<ReadFn>("sevk_guest_readback")(context, output, pin.AddrOfPinnedObject(), (ulong)result.Length)); } finally { pin.Free(); }
            for (int i = 0; i < result.Length; i += 4)
            {
                byte value = oddExpected.HasValue && ((i / 4 % 8 + i / 32) % 2 != 0) ? oddExpected.Value : expected;
                if (Math.Abs(result[i] - value) > 2 || Math.Abs(result[i + 1] - value) > 2 || Math.Abs(result[i + 2] - value) > 2)
                    throw new Exception("lit mip GPU mismatch at LOD " + lod + ": " + result[i] + " expected " + value);
            }
        }
        Upload(4, 0);
        for (int lod = 1; lod <= 3; lod++) Draw(lod, 128);
        Draw(0.5f, 191, 64);
        Upload(1, 1);
        for (int lod = 0; lod <= 3; lod++) Draw(lod, 64);
        Console.WriteLine("GPU PASS: FP16 HDR mip averages at LOD 1/2/3, fractional LOD filtering, refreshed base/all mip values");
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] struct Info
    {
        public uint size, abi, platform, validation;
        public IntPtr window;
        public uint width, height, vsync, adapter;
        public IntPtr shaders, cache, extensions;
        public uint extensionCount, reserved;
        public IntPtr surface, user, getProc;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateFn(ref Info info, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PipelineFn(IntPtr context, IntPtr vs, ulong vb, string ve, IntPtr ps, ulong pb, string pe, out ulong id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrorFn(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DestroyFn(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ImageFn(IntPtr context, ulong id, uint width, uint height, uint depth, uint format);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int UpdateFn(IntPtr context, ulong id, IntPtr data, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ResetFn(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FeaturesFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SubmitFn(IntPtr context, IntPtr packets, uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReplayFn(IntPtr context, uint mark);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ReadFn(IntPtr context, ulong id, IntPtr rgba, ulong bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StatsFn(IntPtr context, ref Stats stats);
    [StructLayout(LayoutKind.Sequential)] struct Stats
    {
        public uint size, abi, api, validation;
        public ulong submitted, presented, meshDraws, spriteDraws, uploaded, textures, meshes, validationErrors, completed;
        public uint width, height, surfaceReady, deviceLost;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string device;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string detail;
    }
}
