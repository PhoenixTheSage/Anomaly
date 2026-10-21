using System;
using System.Linq;
using ClientPlugin.Backend;

static partial class NativeProbe
{
    public static void CompositionContracts()
    {
        if(!Enabled)return;
        var helpers=GuestPassList.Snapshot().Where(p=>p.Helper).ToArray();
        const ulong source=84301,dest=84302,output=84303;
        ImageCreate(source,3,2,1,1);ImageCreate(dest,7,5,1,1);ImageCreate(output,7,5,1,1);
        void Upload(ulong image,int width,int height,float[] value)
        {
            var bytes=new byte[width*height*8];
            for(int pixel=0;pixel<width*height;pixel++)for(int channel=0;channel<4;channel++)
                Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)value[channel])),0,bytes,pixel*8+channel*2,2);
            ImageUpdate(image,bytes);
        }
        int checks=0;
        foreach(float alpha in new[]{-1f,0f,.25f,1f,2f})
        foreach(int mode in new[]{0,1,2,3})
        {
            var s=new[]{2.5f,-.25f,.375f,alpha};var d=new[]{.8f,1.25f,4f,.7f};
            Upload(source,3,2,s);Upload(dest,7,5,d);
            var sampled=new ulong[14];sampled[0]=source;sampled[1]=dest;
            ulong pipeline=helpers.Single(p=>p.Id=="anomaly.helper.Merge"+mode).Pipeline;
            ResetQueue();Queue(1,5,7,5,pipeline,output,Array.Empty<byte>(),sampled,0);Replay(1);
            var result=ReadRaw(output,7*5*8);
            // Independent equations, not values evaluated by the shader under test.
            for(int pixel=0;pixel<35;pixel++)for(int channel=0;channel<4;channel++)
            {
                float expected=mode==0?s[channel]:mode==1?(channel==3?d[3]:s[channel]+d[channel]):
                    mode==2?s[channel]+d[channel]*(1-Math.Clamp(s[3],0,1)):
                    channel==3?d[3]:d[channel]*(1-Math.Clamp(s[channel],0,1));
                float actual=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(result,pixel*8+channel*2));
                if(Math.Abs(actual-expected)>.005f)throw new Exception($"Merge{mode} alpha={alpha} channel={channel}: {actual} != {expected}");
                checks++;
            }
        }
        ResetQueue();Console.WriteLine("GPU PASS: "+checks+" independent composition pixel checks, all merge modes, saturated/out-of-range opacity, destination alpha, HDR and odd-size scaling");
    }
}
