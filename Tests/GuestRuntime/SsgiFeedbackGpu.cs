using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Backend;

static partial class NativeProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SsgiClipFn(IntPtr context,float farPlane);

    // Executes unmodified registered pack shaders. Trace input and scene fields
    // are controlled; this isolates feedback/composition from stochastic tracing.
    public static void SsgiFeedbackGpu(string assets,string root)
    {
        if(!Enabled)throw new Exception("SSGI feedback requires --gpu and real hardware");
        string pack=Path.GetFullPath(Path.Combine(root,"../Prism.SSGI/Assets/AnomalyPack"));
        GuestPassList.RegisterRoot("ssgi.feedback",pack);
        try
        {
            GuestCompiler.CompilePending(assets);
            var passes=GuestPassList.Snapshot().Where(p=>p.Id.StartsWith("prism.ssgi.",StringComparison.Ordinal)).ToDictionary(p=>p.Id);
            foreach(var pass in passes.Values)if(pass.Pipeline==0)throw new Exception(pass.Id+": "+pass.Status+" "+pass.Error);
            if(passes["prism.ssgi.colorHistory"].HistoryName!="ssgi.colorHistory" ||
                !string.IsNullOrEmpty(passes["prism.ssgi.temporal"].HistoryName))
                throw new Exception("The registered manifest must retain filtered color through its separate history owner");
            Check(Fn<SsgiClipFn>("sevk_set_view_clip")(context,10000));
            int scenario=0,checks=0;
            void Require(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
            foreach(int size in new[]{4,2})foreach(int iterations in new[]{0,3,7})
            {
                ulong first=85600+(ulong)(scenario++*100);
                ulong sceneDepth=first,normal=first+1,velocity=first+2,albedo=first+3,metal=first+4,trace=first+5,
                    historyRead=first+6,historyWrite=first+7,temporal=first+8,momentsRead=first+9,momentsWrite=first+10,
                    depthRead=first+11,depthWrite=first+12,normalRead=first+13,normalWrite=first+14,
                    spatialA=first+15,spatialB=first+16,composite=first+17;
                foreach(ulong id in new[]{sceneDepth,normal,albedo,metal,composite})ImageCreate(id,8,8,1,1);
                ImageCreate(velocity,8,8,1,3);ImageUpdate(velocity,new byte[8*8*4]);
                foreach(ulong id in new[]{trace,historyRead,historyWrite,temporal,momentsRead,momentsWrite,depthRead,depthWrite,normalRead,normalWrite,spatialA,spatialB})
                {ImageCreate(id,size,size,1,1);ImageUpdate(id,new byte[size*size*8]);}
                Upload(sceneDepth,8,(_,_)=>new[]{5f,0f,0f,0f});
                Upload(normal,8,(_,_)=>new[]{.5f,.5f,0f,0f});
                Upload(albedo,8,(_,_)=>new[]{1f,1f,1f,0f});
                float[] previousFiltered=null;
                for(int frame=0;frame<3;frame++)
                {
                    var begin=new byte[144];for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes(1f),0,begin,i*20,4);
                    var pin=GCHandle.Alloc(begin,GCHandleType.Pinned);
                    try{Check(Fn<ResourceFn>("sevk_begin_frame")(context,pin.AddrOfPinnedObject()));}finally{pin.Free();}
                    Check(Fn<EndFrameFn>("sevk_end_frame")(context));
                    // Frame 2 deliberately revokes prior-depth coverage while color
                    // and moments remain nonzero: it must seed instead of blend.
                    if(frame==2)ImageUpdate(depthRead,new byte[size*size*8]);
                    Upload(trace,size,(x,y)=>{float v=frame==0?((x+y)%2==0?.4f:.6f):frame==1?.8f:.2f;return new[]{v,v,v,0f};});
                    Upload(metal,8,(_,_)=>new[]{frame==1?1f:0f,0f,0f,0f});
                    var inputs=new ulong[14];inputs[1]=sceneDepth;inputs[2]=velocity;inputs[4]=albedo;inputs[5]=normal;inputs[6]=metal;
                    inputs[7]=historyRead;inputs[8]=trace;inputs[9]=depthRead;inputs[12]=normalRead;inputs[13]=momentsRead;
                    byte[] uniforms=new byte[16];Buffer.BlockCopy(BitConverter.GetBytes(16f),0,uniforms,0,4);
                    ResetQueue();
                    Draw("temporal",temporal,size,inputs,uniforms);
                    Draw("moments",momentsWrite,size,inputs,uniforms);
                    ulong filtered=temporal;
                    for(int i=0;i<iterations;i++)
                    {
                        var filterInputs=(ulong[])inputs.Clone();filterInputs[7]=filtered;
                        var step=new byte[16];Buffer.BlockCopy(BitConverter.GetBytes((float)(1<<(iterations-1-i))),0,step,0,4);
                        ulong target=i%2==0?spatialA:spatialB;
                        Draw("atrous"+i,target,size,filterInputs,step);filtered=target;
                    }
                    var copyInputs=(ulong[])inputs.Clone();copyInputs[7]=filtered;
                    Draw("colorHistory",historyWrite,size,copyInputs,Array.Empty<byte>());
                    Draw("composite",composite,8,copyInputs,Array.Empty<byte>());
                    Draw("prevDepth",depthWrite,size,inputs,Array.Empty<byte>());
                    Draw("prevNormal",normalWrite,size,inputs,Array.Empty<byte>());
                    Replay(1);
                    var tracePixels=Pixels(trace,size);var temporalPixels=Pixels(temporal,size);var filteredPixels=Pixels(filtered,size);
                    Require(ReadRaw(filtered,size*size*8).SequenceEqual(ReadRaw(historyWrite,size*size*8)),"history must exactly retain filtered radiance and variance");
                    for(int p=0;p<size*size;p++)for(int c=0;c<3;c++)
                    {
                        float expected=frame==1?(previousFiltered[p*4+c]+tracePixels[p*4+c])*.5f:tracePixels[p*4+c];
                        Require(Math.Abs(temporalPixels[p*4+c]-expected)<.003f,"temporal feedback/reset mismatch at size="+size+" filters="+iterations+" frame="+frame+" pixel="+p+" actual="+temporalPixels[p*4+c]+" expected="+expected);
                    }
                    if(frame==0 && iterations>0)
                        Require(Enumerable.Range(0,size*size).Any(p=>Math.Abs(filteredPixels[p*4]-temporalPixels[p*4])>.005f),"pattern must distinguish spatially filtered history from the temporal intermediate");
                    var composed=Pixels(composite,8);
                    Require(composed.All(float.IsFinite),"composite has nonfinite pixels");
                    Require(frame==1?composed.All(v=>Math.Abs(v)<.001f):composed.Where((_,i)=>i%4<3).All(v=>v>.1f),"diffuse/metal composite gate mismatch");
                    var moments=Pixels(momentsWrite,size);
                    for(int p=0;p<size*size;p++)Require(Math.Abs(moments[p*4+2]-(frame==1?2:1))<.001f,"moments age must agree with accepted/rejected history");
                    previousFiltered=filteredPixels;
                    (historyRead,historyWrite)=(historyWrite,historyRead);(momentsRead,momentsWrite)=(momentsWrite,momentsRead);
                    (depthRead,depthWrite)=(depthWrite,depthRead);(normalRead,normalWrite)=(normalWrite,normalRead);
                }
                Console.WriteLine("GPU PASS: SSGI production temporal/filter/history/composite feedback size="+size+" iterations="+iterations);
            }
            Console.WriteLine("GPU PASS: "+checks+" SSGI feedback checks; half/quarter scale, 0/3/7 filters, exact filtered history, depth-reset seeding, moments age and diffuse/metal composition");

            void Draw(string suffix,ulong target,int size,ulong[] sampled,byte[] uniforms)
            {var program=passes["prism.ssgi."+suffix];Queue(1,5,size,size,program.Pipeline,target,uniforms,sampled,program.Flags);}
        }
        finally{GuestPassList.UnregisterRoot("ssgi.feedback");ResetQueue();}
    }

    static void Upload(ulong id,int size,Func<int,int,float[]> value)
    {
        var bytes=new byte[size*size*8];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {var pixel=value(x,y);for(int c=0;c<4;c++)Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)pixel[c])),0,bytes,((y*size+x)*4+c)*2,2);}
        ImageUpdate(id,bytes);
    }
    static float[] Pixels(ulong id,int size)
    {
        var bytes=ReadRaw(id,size*size*8);var pixels=new float[size*size*4];
        for(int i=0;i<pixels.Length;i++)pixels[i]=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*2));return pixels;
    }
}
