using System;
using System.Reflection;

namespace ClientPlugin.Backend;

/// <summary>
/// Talks to SE-Vulkan <c>ClientPlugin.Rendering.GuestBridge</c> by reflection.
/// Anomaly never records a Vulkan command buffer.
/// </summary>
static class GuestBridgeClient
{
    const string TypeName = "ClientPlugin.Rendering.GuestBridge";
    static ulong nextImage = 1;
    static object[] shadowInputs;
    static bool haveShadowInputs;
    static long colorHistoryEpoch;
    static uint colorHistoryFrame=uint.MaxValue;
    static ulong colorHistoryImage;
    static readonly System.Collections.Generic.List<Tuple<long, ulong>> RetiredImages = new System.Collections.Generic.List<Tuple<long, ulong>>();
    internal static void RetireImage(ulong image, long epoch)
    {
        if (image == 0 || epoch == 0) return;
        lock (RetiredImages) RetiredImages.Add(Tuple.Create(epoch, image));
    }
    internal static void ResetDevice(){haveShadowInputs=false;shadowInputs=null;VolumeIntervalsReady=false;colorHistoryEpoch=0;colorHistoryFrame=uint.MaxValue;colorHistoryImage=0;GuestStageBindings.Reset();}
    internal static bool VolumeIntervalsReady { get; private set; }
    static readonly string[] VolumeProducts={"volumeScatteringTransmittance","volumeMotion","volumeReactive","volumeRepresentativeDepth","volumeSunVisibility"};

    public static void Reset()
    {
        Call("Reset");
    }

    public static bool InitGBufferAttachment(int slot, int format, byte[] pixel)
    {
        var value = Call("InitGBufferAttachment", slot, format, pixel);
        return value is bool ok && ok;
    }

    public static void PublishAttachment(string name, int slot)
    {
        if (string.IsNullOrEmpty(name) || slot < 0 || slot > 2)
            return;
        ulong first=SupportsPacketFlags(SharedFrame)?Attach0:0xFFFF000000000009UL;
        ClientPlugin.Buffers.BufferCatalog.Set(name, new ClientPlugin.Buffers.GuestRendererImage(first + (ulong)slot, SceneView.Width, SceneView.Height, 1));
    }

    internal const int ClassicNullResources = 8388608;
    /// <summary>Packet flag marking a Display-policy AfterUpscale program (HDR display tenancy).</summary>
    internal const int DisplayGrade = 16384;

    public static ulong CreatePipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry)
    {
        var value = Call("CreatePipeline", vs, ps, vsEntry, psEntry);
        return value is ulong id ? id : 0;
    }

    public static ulong CreateExtendedPipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry, int contract)
    {
        if (!SupportsPacketFlags(512)) return 0;
        var value = Call("CreateExtendedPipeline", vs, ps, vsEntry, psEntry, contract);
        return value is ulong id ? id : 0;
    }

    public static bool SubmitExtended(int mark, int compose, int width, int height, int depth, ulong pipeline, ulong[] outputs, byte[][] constants, ulong[] sampled, int flags = 0)
    {
        if (!SupportsPacketFlags(512)) return false;
        return Call("SubmitExtended", mark, compose, width, height, depth, pipeline, outputs, constants, sampled, flags) is true;
    }
    public static bool SubmitExtendedBatch(object[][] passes)=>SupportsPacketFlags(512) && Call("SubmitExtendedBatch",(object)passes) is true;

    public static void CreateImage(ulong id, int width, int height, int depth, int format)
    {
        Call("CreateImage", id, width, height, depth, format);
    }

    public static void UpdateImage(ulong id, byte[] data)
    {
        Call("UpdateImage", id, data);
    }

    internal const int ResourceCommands = 8192;
    internal const int OwnedRasterGenerations = 262144;
    internal const int EarlyOwnedProducers = 2097152;
    internal static byte[] ExecutionStatus(ulong pipeline) => Call("ExecutionStatus", pipeline) as byte[];
    internal static object[] CaptureFrameInputs() => Call("CaptureFrameInputs") as object[];
    internal static bool SetFrameEnvironment(byte[] packet) => Call("SetFrameEnvironment", (object)packet) is true;
    internal static bool ResourceCommand(int operation, ulong image, int mark = 1, float[] value = null)
    {
        if (!SupportsPacketFlags(ResourceCommands))
            throw new NotSupportedException("Update SE-Vulkan native library: target clears and retirement require guest feature bit13.");
        var packet = new byte[48];
        System.Buffer.BlockCopy(new[] {48, 1, operation, mark}, 0, packet, 0, 16);
        System.Buffer.BlockCopy(BitConverter.GetBytes(image), 0, packet, 16, 8);
        if (value != null) System.Buffer.BlockCopy(value, 0, packet, 32, 16);
        return Call("ResourceCommand", (object)packet) is true;
    }

    public static ulong AllocateImage(int width, int height, int depth, byte[] rgba, int format = 0)
    {
        ulong id = nextImage++;
        CreateImage(id, width, height, depth, format);
        if (rgba != null)
            UpdateImage(id, rgba);
        return id;
    }

    public static void InvalidateHistory() => Call("InvalidateHistory");

    static ulong PinImage(ulong id, int width, int height, int depth, int format)
    {
        CreateImage(id, width, height, depth, format);
        return id;
    }

    static ulong PinnedStorage(GuestProgram program)
    {
        string id = program == null ? "" : program.Id ?? "";
        if (id.EndsWith("CameraVelocity", StringComparison.Ordinal))
            return CatalogVelocity;
        if (id.EndsWith("ReactiveClear", StringComparison.Ordinal))
            return CatalogReactiveMask;
        string output = program == null ? "" : program.OutputName ?? "";
        if (string.Equals(output, "volumeReactive", StringComparison.OrdinalIgnoreCase))
            return CatalogVolumeReactive;
        return 0;
    }

    public static ulong AllocateVolumeMips(int dim, byte[][] mips)
        =>UploadVolumeMips(0,dim,mips);

    internal static ulong UploadVolumeMips(ulong image,int dim,byte[][] mips)
    {
        if (mips == null || mips.Length == 0 || dim <= 0)
            return 0;
        int bytes = 0;
        for (int i = 0; i < mips.Length; i++)
            bytes += mips[i] == null ? 0 : mips[i].Length;
        var packed = new byte[bytes];
        int offset = 0;
        for (int i = 0; i < mips.Length; i++)
        {
            if (mips[i] == null)
                continue;
            System.Buffer.BlockCopy(mips[i], 0, packed, offset, mips[i].Length);
            offset += mips[i].Length;
        }
        int format = (mips.Length << 16);
        if(image==0)return AllocateImage(dim,dim,dim,packed,format);
        CreateImage(image,dim,dim,dim,format);UpdateImage(image,packed);return image;
    }

    // The reserved color alias is immutable for this frame, including callbacks.
    // Require an exact previous producer and reject resized/recreated storage.
    internal static ulong PreviousColor(System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        long epoch=ResourceEpoch;
        if(programs==null||!GuestFrame.Open||epoch==0)return 0;
        uint frame=GuestFrame.U32(16);
        if(colorHistoryEpoch==epoch&&colorHistoryFrame==frame)return colorHistoryImage;
        colorHistoryEpoch=epoch;colorHistoryFrame=frame;colorHistoryImage=0;
        if(!GuestFrame.History)return 0;
        foreach(var producer in programs)
        {
            if(producer==null||producer.Id!="anomaly.owned.HistoryCopy"||!producer.Enabled||producer.ConflictDisabled||producer.Pipeline==0||producer.ResourceEpoch!=epoch)continue;
            PassSize(producer,out int width,out int height);
            if(producer.ImageWidth!=width||producer.ImageHeight!=height)return 0;
            var image=ClientPlugin.Buffers.BufferCatalog.Active("historyColor") as ClientPlugin.Buffers.GuestRendererImage;
            if(image==null||!image.IsAvailable||(image.ImageId!=producer.StorageImage&&image.ImageId!=producer.HistoryImage))return 0;
            var execution=ExecutionStatus(producer.Pipeline);
            if(execution!=null&&execution.Length==48&&BitConverter.ToUInt64(execution,16)!=0&&unchecked(frame-BitConverter.ToUInt32(execution,40))==1)
                colorHistoryImage=image.ImageId;
            break;
        }
        return colorHistoryImage;
    }

    public static void Submit(System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        ulong previousColor=PreviousColor(programs);
        if(previousColor==0)ClientPlugin.Buffers.BufferCatalog.Set("historyColor",null);
        var bindings=GuestStageBindings.Freeze(programs);
        GuestVolumes.BeginFrame();
        VolumeIntervalsReady=false;
        try { VolumeRegistryState.ResetIntervals(); VolumeIntervalsReady=true; }
        catch(Exception error) { VolumeRegistryState.SetStatus("Shared volume interval reset failed: "+error.Message); }
        foreach(string name in VolumeProducts)
            ClientPlugin.Buffers.BufferCatalog.Set(name,null);
        try { Reset(); }
        catch { VolumeIntervalsReady=false;throw; }
        GuestCatalogTargets.Flush();
        lock (RetiredImages)
        {
            foreach (var retired in RetiredImages) if (retired.Item1 == ResourceEpoch) ResourceCommand(1, retired.Item2);
            RetiredImages.Clear();
        }
        foreach (var retired in GuestPassList.TakeRetired())
        {
            if (retired.ResourceEpoch != ResourceEpoch) continue;
            if (retired.Pipeline != 0) ResourceCommand(2, retired.Pipeline);
            if (retired.StorageImage != 0 && retired.StorageImage < Hdr) ResourceCommand(1, retired.StorageImage);
            if (retired.HistoryImage != 0 && retired.HistoryImage != retired.StorageImage && retired.HistoryImage < Hdr) ResourceCommand(1, retired.HistoryImage);
        }
        haveShadowInputs = false; shadowInputs = null;
        ClientPlugin.Buffers.BufferCatalog.Set("occupancy", null);
        ClientPlugin.Buffers.BufferCatalog.Set("pointShadowAtlas", null);
        ClientPlugin.Buffers.BufferCatalog.Set("playerDepth", null);
        if (programs == null)
            return;
        foreach (var p in programs) if (p != null) p.SubmittedThisFrame = false;
        int litLevels = GuestOwned.LitMipLevels(programs, SceneView.Width, SceneView.Height);
        if (litLevels == 0) ClientPlugin.Buffers.BufferCatalog.Set("litMips", null);
        // Allocate first, then freeze every previous-frame alias. Registration
        // and consumer order cannot change a history's age within this frame.
        foreach(var program in programs)
        {
            if(program==null || program.Helper || program.ResourceContract!=0 ||
                program.Id==GuestOccupancy.SplatId || program.Id==GuestOccupancy.StampId ||
                program.Id==GuestPointShadows.GeometryId || program.Id==GuestPointShadows.HeaderId)continue;
            bool active=program.Enabled && !program.ConflictDisabled && program.Pipeline!=0;
            if(!active)
            {
                program.HistoryReady=false;
                if(!string.IsNullOrEmpty(program.HistoryName))ClientPlugin.Buffers.BufferCatalog.Set(program.HistoryName,null);
                ClientPlugin.Buffers.BufferCatalog.Set("pass."+program.Id,null);
                if(!string.IsNullOrEmpty(program.OutputName))ClientPlugin.Buffers.BufferCatalog.Set(program.OutputName,null);
                continue;
            }
            bool litMips=program.Id=="anomaly.owned.LitMips";
            if(litMips && litLevels==0)continue;
            if((program.Flags&DepthTest)==0)
            {
                PassSize(program,out int width,out int height);
                bool ordinary=!program.Id.StartsWith("anomaly.owned.",StringComparison.Ordinal) && (program.Flags&Compute)==0;
                int format=(ordinary?1:program.Format)|(litMips?litLevels<<16:0);
                if(program.StorageImage==0 || program.ImageWidth!=width || program.ImageHeight!=height || program.ImageFormat!=format)
                {
                    ulong pinned=PinnedStorage(program);
                    program.StorageImage=pinned!=0?PinImage(pinned,width,height,1,format):program.StorageImage!=0?
                        PinImage(program.StorageImage,width,height,1,format):AllocateImage(width,height,1,null,format);
                    if(!string.IsNullOrEmpty(program.HistoryName))program.HistoryImage=program.HistoryImage!=0?
                        PinImage(program.HistoryImage,width,height,1,format):AllocateImage(width,height,1,null,format);
                    program.ImageWidth=width;program.ImageHeight=height;program.ImageFormat=format;program.HistoryReady=false;
                }
                else if(!string.IsNullOrEmpty(program.HistoryName) && program.HistoryImage==0)
                {program.HistoryImage=AllocateImage(width,height,1,null,format);program.HistoryReady=false;}
            }
            if(program.HistoryReady && GuestFrame.Open && ResourceEpoch!=0)
            {
                var execution=ExecutionStatus(program.Pipeline);
                program.HistoryReady=GuestFrame.History && execution!=null && execution.Length==48 && BitConverter.ToUInt64(execution,16)!=0 &&
                    BitConverter.ToUInt32(execution,40)==program.HistoryFrame && unchecked(GuestFrame.U32(16)-program.HistoryFrame)==1;
            }
            if(!string.IsNullOrEmpty(program.HistoryName))ClientPlugin.Buffers.BufferCatalog.Set(program.HistoryName,
                program.HistoryReady && program.HistoryImage!=0?new ClientPlugin.Buffers.GuestRendererImage(program.HistoryImage,program.ImageWidth,program.ImageHeight,1):null);
        }
        GuestVolumes.TrySubmit(programs);
        var callbacks=PortablePassRegistry.Snapshot();
        Action captureColorHistory=null;
        for(int currentMark=GuestMark.AfterLighting;currentMark<=GuestMark.AfterUpscale;currentMark++)
        {
        ulong previous=currentMark>=5?Upscale:Hdr;bool lastChain=false;
        PortablePassRegistry.Run(callbacks,currentMark,0,programs);
        foreach (var program in Ordered(programs))
        {
            if(program==null || program.Mark!=currentMark)continue;
            if(program==null || program.Helper)continue;
            GuestPassList.RefreshPreparedInputs(program);
            if(GuestVolumes.SuppressFallback(program))continue;
            if (GuestOccupancy.Handles(program, programs) || GuestPointShadows.Handles(program, programs)) continue;
            if (!program.Enabled || program.ConflictDisabled || program.Pipeline == 0 || program.Mark == 0 || program.ResourceContract != 0)continue;
            if(program.Mark==GuestMark.AfterUpscale && ResourceEpoch!=0 && !SupportsPacketFlags(65536))
            {program.Status="capability-missing";program.Error="Update SE-Vulkan: correct AfterUpscale output requires guest feature bit16";continue;}
            bool litMips=program.Id=="anomaly.owned.LitMips";
            if(litMips && litLevels==0)continue;
            if(litMips && !SupportsPacketFlags(GenerateMips))
            {ClientPlugin.Buffers.BufferCatalog.Set("litMips",null);program.Status="capability-missing";program.Error="Update SE-Vulkan: guest lit mip generation is unavailable";continue;}
            if(litMips && program.Status=="capability-missing"){program.Status="pipeline";program.Error=null;}
            int compose=ComposeOf(program.Compose);
            bool offscreen=(program.Flags&DepthTest)==0;
            bool ordinary=!program.Id.StartsWith("anomaly.owned.",StringComparison.Ordinal);
            if(offscreen && ordinary && compose!=5 && MergePipeline(programs,compose==3?0:MergeMode(compose))==0)
            {program.Error="Fullscreen composition helper is not ready";continue;}
            PassSize(program,out int width,out int height);
            ulong storage=offscreen?program.StorageImage:Hdr;
            if(storage==0)continue;
            var sampled=ImagesFor(program,previous,compose);
            // A pack always writes its isolated product without blending. The
            // independent DX11 merge equations apply afterward at destination size.
            int flags=program.Flags;
            if(SupportsPacketFlags(EarlyOwnedProducers)&&IsEarlyOwned(program))flags|=4096;
            // Display-policy AfterUpscale programs: the renderer grades them at the tone position on an HDR display.
            if(program.Mark==GuestMark.AfterUpscale&&(program.Policy&8)!=0)flags|=DisplayGrade;
            if(program.Id=="anomaly.owned.HistoryCopy")
            {
                // Copy immutable command values now, then append capture after all
                // public late draws and AfterFullscreen callbacks, matching DX11.
                var command=new object[]{program.Mark,offscreen?5:compose,width,height,program.Pipeline,storage,PackUniforms(program.Uniforms),sampled,flags};
                captureColorHistory=()=>{
                    Call("Submit",command);
                    program.Submissions++;program.SubmittedThisFrame=true;program.Error=null;
                    if(offscreen){PublishOwned(program);PublishPass(program,storage,width,height);}
                    if(!string.IsNullOrEmpty(program.HistoryName)&&program.HistoryImage!=0)
                    {
                        ulong written=program.StorageImage;program.StorageImage=program.HistoryImage;program.HistoryImage=written;program.HistoryReady=true;
                        program.HistoryFrame=GuestFrame.Open?GuestFrame.U32(16):uint.MaxValue;
                    }
                };
                continue;
            }
            Call("Submit",program.Mark,offscreen?5:compose,width,height,program.Pipeline,storage,PackUniforms(program.Uniforms),sampled,flags);
            program.Submissions++;program.SubmittedThisFrame=true;program.Error=null;
            if(offscreen)
            {
                PublishOwned(program);PublishPass(program,storage,width,height);
                if(ordinary)
                {
                    GuestTemporal.FromIsolated(programs,program,storage);
                    previous=storage;lastChain=compose==3;
                    if(compose!=3 && compose!=5)Merge(programs,program.Mark,storage,MergeMode(compose),flags&DisplayGrade);
                }
            }
            if(!string.IsNullOrEmpty(program.HistoryName) && program.HistoryImage!=0)
            {
                ulong written=program.StorageImage;program.StorageImage=program.HistoryImage;program.HistoryImage=written;program.HistoryReady=true;
                program.HistoryFrame=GuestFrame.Open?GuestFrame.U32(16):uint.MaxValue;
            }
        }
        if(lastChain)Merge(programs,currentMark,previous,0);
        PortablePassRegistry.Run(callbacks,currentMark,1,programs);
        }
        captureColorHistory?.Invoke();
        GuestStageBindings.Submit(bindings);
        Call("SetNativeSunGlare", !OwnsGlare(programs));
    }

    static bool IsEarlyOwned(GuestProgram p)=>p.Id=="anomaly.owned.CameraVelocity"||p.Id=="anomaly.owned.LinearDepth"||p.Id=="anomaly.owned.HiZ"||p.Id=="anomaly.owned.ReactiveClear";
    static int MergeMode(int compose)=>compose==1?2:compose==2?3:compose==4?0:1;
    internal static void ExecutePortable(int mark,object[][] commands,System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        if(commands.Length!=0 && !SupportsPacketFlags(131072))
            throw new NotSupportedException("Update SE-Vulkan native library: portable rendering commands require guest feature bit17.");
        ulong Image(string name,out int width,out int height)
        {
            if(name==null || name=="sceneColor")
            {
                PassSize(new GuestProgram{Mark=mark,Scale=1,Compose="Replace"},out width,out height);return Hdr;
            }
            object image=ClientPlugin.Buffers.BufferCatalog.Active(name);
            if(image is ClientPlugin.Buffers.GuestRendererImage guest && guest.IsAvailable){width=guest.Width;height=guest.Height;return guest.ImageId;}
            if(image is ClientPlugin.Buffers.PublishedBuffer published && published.IsAvailable&&!published.IsStructured){width=published.Width;height=published.Height;return published.ImageId;}
            throw new InvalidOperationException("Portable image is unavailable: "+name);
        }
        ulong Helper(string name)
        {
            foreach(var p in programs)if(p.Helper && p.Id=="anomaly.helper."+name && p.Pipeline!=0)return p.Pipeline;
            throw new InvalidOperationException("Portable helper is unavailable: "+name);
        }
        foreach(var command in commands)
        {
            int operation=(int)command[0];
            if(operation==0)
            {
                string id=(string)command[1];GuestProgram selected=null;
                foreach(var p in programs)if(p.Id==id && !p.Helper)selected=p;
                if(selected==null || selected.Pipeline==0 || selected.ResourceContract!=0 || (selected.Flags&(Compute|DepthTest))!=0)
                    throw new InvalidOperationException("Portable fullscreen program is unavailable or requires a different execution contract: "+id);
                GuestPassList.RefreshPreparedInputs(selected);
                ulong target=Image(command[2] as string,out int width,out int height);
                var draw=selected.FrameCopy();draw.Mark=mark;
                var inputs=ImagesFor(draw,mark>=5?Upscale:Hdr,0);
                if(command[3] is string scene)inputs[0]=Image(scene,out _,out _);
                if(target!=Hdr && Array.IndexOf(inputs,target)>=0)
                    throw new InvalidOperationException("Portable fullscreen cannot sample its private target; copy it to a separate catalog image first: "+id);
                Call("Submit",mark,4,width,height,selected.Pipeline,target,PackUniforms(selected.Uniforms),inputs,selected.Flags);
                selected.Submissions++;selected.SubmittedThisFrame=true;selected.Status="submitted";
                continue;
            }
            if(operation==1)
            {
                ulong source=Image((string)command[1],out _,out _),target=Image((string)command[2],out int width,out int height);
                if(source==target)continue;
                var inputs=new ulong[14];inputs[0]=source;Call("Submit",mark,4,width,height,Helper("Merge0"),target,Array.Empty<byte>(),inputs,0);continue;
            }
            if(operation==2)
            {
                ulong target=Image((string)command[1],out int width,out int height);
                Call("Submit",mark,4,width,height,Helper("ClearColor"),target,PackUniforms((float[])command[2]),new ulong[14],0);continue;
            }
            if(operation==3)
            {
                GuestTemporal.Contribute(programs,mark,Image((string)command[1],out _,out _),Image((string)command[2],out _,out _));continue;
            }
            throw new InvalidOperationException("Unknown portable command: "+operation);
        }
    }
    static ulong MergePipeline(System.Collections.Generic.IReadOnlyList<GuestProgram> programs,int mode)
    {
        foreach(var p in programs)if(p!=null && p.Helper && p.Id=="anomaly.helper.Merge"+mode)return p.Pipeline;
        return 0;
    }
    // A Display program's merge carries DisplayGrade so the renderer composites it at the tone position too.
    static bool Merge(System.Collections.Generic.IReadOnlyList<GuestProgram> programs,int mark,ulong source,int mode,int flags=0)
    {
        ulong pipeline=MergePipeline(programs,mode);if(pipeline==0)return false;
        var dimensions=new GuestProgram{Mark=mark,Scale=1f,Compose="Replace"};PassSize(dimensions,out int width,out int height);
        var sampled=new ulong[14];sampled[0]=source;sampled[1]=mark>=5?Upscale:Hdr;
        Call("Submit",mark,4,width,height,pipeline,Hdr,Array.Empty<byte>(),sampled,flags);return true;
    }

    static System.Collections.Generic.IEnumerable<GuestProgram> Ordered(System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        // A consumer can register before Anomaly.LoadAssets. Publish producers
        // first, then preserve pack order within each native insertion mark.
        var sorted = new System.Collections.Generic.List<GuestProgram>(programs);
        // Stable insertion sort retains manifest order when priorities tie.
        for (int i = 1; i < sorted.Count; i++)
        {
            var item = sorted[i];
            int j = i - 1;
            while (j >= 0 && (sorted[j]?.Priority ?? 0) > (item?.Priority ?? 0))
            { sorted[j + 1] = sorted[j]; j--; }
            sorted[j + 1] = item;
        }
        for (int mark = GuestMark.AfterLighting; mark <= GuestMark.AfterUpscale; mark++)
        {
            for (int owned = 1; owned >= 0; owned--)
                for (int i = 0; i < sorted.Count; i++)
                {
                    var p = sorted[i];
                    if (p != null && p.Mark == mark && (p.Id != null && p.Id.StartsWith("anomaly.owned.", StringComparison.Ordinal) ? 1 : 0) == owned)
                        yield return p;
                }
        }
    }

    static bool OwnsGlare(System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        for (int i = 0; i < programs.Count; i++)
        {
            var program = programs[i];
            if (program == null || !program.Enabled || program.Pipeline == 0 || (program.Flags & DepthTest) == 0 || (program.Flags & ProbeFace) != 0)
                continue;
            if (!GuestColor.NativeGlare(program.Id))
                return true;
        }
        return false;
    }

    internal const string CelestialProbeSuffix = ".probe";
    /// <summary>Native owner capability flag64: probe-face celestial passes.</summary>
    internal const ulong CelestialProbeFeature = 64;
    public const ulong Hdr = 0xFFFF000000000001UL;
    public const ulong Depth = 0xFFFF000000000002UL;
    public const ulong GBuffer0 = 0xFFFF000000000003UL;
    public const ulong GBuffer1 = 0xFFFF000000000004UL;
    public const ulong GBuffer2 = 0xFFFF000000000005UL;
    public const ulong Upscale = 0xFFFF000000000006UL;
    public const ulong Display = 0xFFFF000000000007UL;
    public const ulong Motion = 0xFFFF000000000008UL;
    public const ulong Attach0 = 0xFFFF000000000010UL;
    public const ulong CatalogVelocity = 0xFFFF00000000000AUL;
    public const ulong CatalogVolumeReactive = 0xFFFF00000000000BUL;
    public const ulong CatalogReactiveMask = 0xFFFF00000000000CUL;
    public const int DepthTest = 1;
    public const int Compute = 2;
    public const int Volume = 4;
    public const int GenerateMips = 8;
    public const int ClearCompute = 16;
    public const int PacketConstants = 32;
    public const int Geometry = 64;
    public const int LiveResources = 1024;
    public const int SceneDepth = 2048;
    public const int SharedFrame = 4096;
    internal static long ResourceEpoch=>Convert.ToInt64(Find()?.GetProperty("ResourceEpoch",BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??0L);
    internal static object[] CaptureVolumeScene()=>Call("CaptureVolumeScene") as object[];
    internal static bool ConfigureSceneDepthViews(ulong[] images,double[] worldMatrices)=>
        SupportsPacketFlags(SceneDepth) && Call("ConfigureSceneDepthViews",images,worldMatrices) is true;
    internal static object[] CaptureSceneDepth()=>Call("CaptureSceneDepth") as object[];
    public const int Viewport = 128;
    public const int ClearFar = 256;
    /// <summary>Celestial probe variant: draws on each environment-probe face, never at its frame mark.</summary>
    public const int ProbeFace = 512;

    internal static bool SupportsPacketFlags(int flags)
    {
        var value = Find()?.GetProperty("SupportedPacketFlags", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return value is int supported && (supported & flags) == flags;
    }

    public static bool TryGetFrame(byte[] frame)
        => GuestFrame.TryCopy(frame) || ReadNativeFrame(frame);

    internal static bool ReadNativeFrame(byte[] frame)
    {
        var value = Call("TryGetFrame", frame);
        return value is bool ready && ready;
    }

    static ulong[] ImagesFor(GuestProgram program, ulong previous, int compose)
    {
        var sampled = new ulong[14];
        string id = program.Id ?? "";
        if (id.EndsWith("HiZ", StringComparison.Ordinal))
            sampled[0] = Catalog("linearDepth");
        else if (id.EndsWith("LinearDepth", StringComparison.Ordinal) || id.EndsWith("CameraVelocity", StringComparison.Ordinal))
            sampled[0] = Depth;
        else if (id.EndsWith("HistoryCopy", StringComparison.Ordinal))
            sampled[0] = Hdr;
        else if (compose == 3)
            sampled[0] = previous;
        else
            sampled[0] = program.Mark >= 5 ? Upscale : Hdr;
        sampled[1] = Catalog("linearDepth");
        sampled[2] = Catalog("velocity");
        sampled[3] = Catalog("reactiveMask");
        if (program.Mark < 5)
        {
            sampled[4] = GBuffer0;
            sampled[5] = GBuffer1;
            sampled[6] = GBuffer2;
        }
        sampled[7] = Catalog("historyColor");
        if (program.Mark >= 5)
        {
            sampled[4] = Catalog("avgLuminance");
            sampled[5] = Catalog("bloom");
            sampled[6] = Catalog("dirt");
        }
        if (program.Srvs != null)
        {
            for (int i = 0; i < program.Srvs.Count; i++)
            {
                int slot = program.Srvs[i].Slot;
                if (slot >= 0 && slot < 14)
                    // DX11 publishes shared volume products after the
                    // AfterAtmosphere fullscreen phase has completed.
                    sampled[slot] = program.Mark <= GuestMark.AfterAtmosphere && Array.IndexOf(VolumeProducts,program.Srvs[i].Name)>=0 ? 0 : Catalog(program.Srvs[i].Name);
            }
        }
        if ((program.Flags & DepthTest) != 0)
            sampled[0] = Depth;
        if ((program.Flags & Volume) != 0)
        {
            sampled[8] = Pack(program.OriginX, program.OriginY);
            sampled[9] = Pack(program.OriginZ, program.MotionX);
            sampled[10] = Pack(program.MotionY, program.MotionZ);
            var textures = program.MediumTextures;
            if (textures != null)
            {
                if (textures.Length > 0) sampled[11] = Catalog(textures[0]);
                if (textures.Length > 1) sampled[12] = Catalog(textures[1]);
                if (textures.Length > 2) sampled[13] = Catalog(textures[2]);
            }
        }
        if (id.EndsWith(".present", StringComparison.Ordinal))
            sampled[7] = Catalog("sharedVolume." + id.Substring(0, id.Length - ".present".Length));
        // A celestial probe variant reads its provider's data and art.
        if ((program.Flags & ProbeFace) != 0 && id.EndsWith(CelestialProbeSuffix, StringComparison.Ordinal)) id = id.Substring(0, id.Length - CelestialProbeSuffix.Length);
        if (GuestColor.Celestial.Data.TryGetValue(id, out var data))
            sampled[1] = data;
        if (id.EndsWith("CameraVelocity", StringComparison.Ordinal))
            sampled[1] = Motion;
        if (GuestColor.Celestial.Art.TryGetValue(id, out var art))
            sampled[2] = art;
        return sampled;
    }

    internal static ulong Catalog(string name)
    {
        var active = ClientPlugin.Buffers.BufferCatalog.Active(name);
        if (active is ClientPlugin.Buffers.PublishedBuffer published)
            return published.IsAvailable?published.ImageId:0;
        if (active is ClientPlugin.Buffers.GuestRendererImage image && image.IsAvailable)
            return image.ImageId;
        return 0;
    }

    public static void SetDisplayFlags(int flags) => Call("SetDisplayFlags", flags);

    internal static object[] CaptureShadowInputs(uint localActor)
    {
        if (!haveShadowInputs) { shadowInputs = Call("CaptureShadowInputs", localActor) as object[]; haveShadowInputs = true; }
        return shadowInputs;
    }
    internal static object[] CaptureCharacter(uint localActor) => Call("CaptureCharacter", localActor) as object[];

    internal static void SubmitPacket(int mark, int compose, int width, int height, ulong pipeline, ulong storage, byte[] uniforms, ulong[] sampled, int flags) =>
        Call("Submit", mark, compose, width, height, pipeline, storage, uniforms, sampled, flags);

    public static void SetUpscaleOwner(string id) => Call("SetUpscaleOwner", id ?? "");
    internal static float[] TonemapValues() => Call("TonemapValues") as float[];
    internal static void SetHdrUi(float paperWhite, float shoulder, float peak) => Call("SetHdrUi", paperWhite, shoulder, peak);
    internal static double FrameSeconds() => Call("FrameSeconds") is double seconds ? seconds : 0;
    internal static ulong[] TonemapInputImages() => Call("TonemapInputImages") as ulong[];

    public static void SetViewClip(float farPlane) => Call("SetViewClip", farPlane);

    internal static void PassSize(GuestProgram program, out int width, out int height)
    {
        int sceneW = Math.Max(SceneView.Width, 1);
        int sceneH = Math.Max(SceneView.Height, 1);
        if(program!=null && program.Id!="anomaly.owned.HistoryCopy" && program.Mark==GuestMark.AfterUpscale && GuestFrame.Open)
        {
            sceneW=Math.Max(1,(int)GuestFrame.F32(332));sceneH=Math.Max(1,(int)GuestFrame.F32(336));
        }
        float scale = GuestPassList.SnapScale(program == null ? 1f : program.Scale);
        string compose = program == null ? "" : program.Compose ?? "";
        bool replace = string.Equals(compose, "Replace", StringComparison.OrdinalIgnoreCase);
        if (replace || scale >= 1f)
        {
            width = sceneW;
            height = sceneH;
            return;
        }
        width = Math.Max(1, (int)(sceneW * scale));
        height = Math.Max(1, (int)(sceneH * scale));
    }

    static void PublishPass(GuestProgram program, ulong image, int width, int height)
    {
        if (program == null || image == 0 || string.IsNullOrEmpty(program.Id))
            return;
        var published = new ClientPlugin.Buffers.GuestRendererImage(image, width, height, 1);
        ClientPlugin.Buffers.BufferCatalog.Set("pass." + program.Id, published);
        if (!string.IsNullOrEmpty(program.OutputName)
            && !string.Equals(program.OutputName, "pass." + program.Id, StringComparison.OrdinalIgnoreCase))
            ClientPlugin.Buffers.BufferCatalog.Set(program.OutputName, published);
    }

    static void PublishOwned(GuestProgram program)
    {
        string name = null;
        string id = program.Id ?? "";
        if (id.EndsWith("LinearDepth", StringComparison.Ordinal)) name = "linearDepth";
        else if (id.EndsWith("CameraVelocity", StringComparison.Ordinal)) name = "velocity";
        else if (id.EndsWith("HiZ", StringComparison.Ordinal)) name = "hiZ";
        else if (id.EndsWith("HistoryCopy", StringComparison.Ordinal)) name = "historyColor";
        else if (id.EndsWith("ReactiveClear", StringComparison.Ordinal)) name = "reactiveMask";
        else if (id == "anomaly.owned.LitMips") name = "litMips";
        else if ((program.Flags & Volume) != 0) name = "sharedVolume." + id;
        if (name == null)
            return;
        ClientPlugin.Buffers.BufferCatalog.Set(name, new ClientPlugin.Buffers.GuestRendererImage(program.StorageImage, program.ImageWidth, program.ImageHeight, 1));
    }

    static ulong Pack(float a, float b)
    {
        uint ua = BitConverter.ToUInt32(BitConverter.GetBytes(a), 0);
        uint ub = BitConverter.ToUInt32(BitConverter.GetBytes(b), 0);
        return ua | ((ulong)ub << 32);
    }

    static int ComposeOf(string compose)
    {
        switch ((compose ?? "").Trim().ToLowerInvariant())
        {
            case "isolatedmix": return 1;
            case "isolatedsub": return 2;
            case "chain": return 3;
            case "replace": return 4;
            case "publishonly": return 5;
            case "directadd": return 6;
            default: return 0;
        }
    }

    static byte[] PackUniforms(float[] values)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<byte>();
        int count = Math.Min(values.Length, 64);
        var bytes = new byte[count * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    internal static object Call(string method, params object[] args)
    {
        var type = Find();
        if (type == null)
            return null;
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name != method || candidate.GetParameters().Length != (args?.Length ?? 0))
                continue;
            return candidate.Invoke(null, args);
        }

        return null;
    }

    static Type Find()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(TypeName, false); }
            catch { continue; }
            if (type != null)
                return type;
        }

        return null;
    }
}
