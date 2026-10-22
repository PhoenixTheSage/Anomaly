using System;

namespace ClientPlugin.Contracts;

// Device-free names and format mapping, shared by the core and DX11 satellite.
internal static class CatalogContract
{
    internal const string Velocity="velocity";
    internal const string LinearDepth="linearDepth";
    internal const string ObjectId="objectId";
    internal const string HistoryColor="historyColor";
    internal const string HiZ="hiZ";
    internal const string ReactiveMask="reactiveMask";
    internal const string FullscreenIsolated="fullscreenIsolated";
    internal const string HdrColor="hdrColor";
    internal const string UpscaledColor="upscaledColor";
    internal const string AvgLuminance="avgLuminance";
    internal const string Bloom="bloom";
    internal const string Dirt="dirt";
    internal const string LitMips="litMips";
    internal const string PointLights="pointLights";
    internal const string TileIndices="tileIndices";
    internal const string HistoryDepth="historyDepth";
    internal const string Occupancy="occupancy";
    internal const string PointShadowAtlas="pointShadowAtlas";
    internal const string PlayerDepth="playerDepth";
    internal const string VolumeSunShadow="volumeSunShadow";
    internal static readonly string[] Reserved = { "velocity", "linearDepth", "hiZ", "historyColor", "reactiveMask", "fullscreenIsolated", "hdrColor", "upscaledColor", "avgLuminance", "bloom", "dirt", "litMips", "pointLights", "tileIndices", "historyDepth", "occupancy", "pointShadowAtlas", "playerDepth" };
    internal static bool IsReserved(string name)
    {
        if(string.IsNullOrWhiteSpace(name))return true;
        foreach(string item in Reserved)if(string.Equals(item,name.Trim(),StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }
    internal static bool RgbaBytes(byte[] bytes,int width,int height,int depth)
    {
        if(bytes==null||width<1||height<1||depth<1)return false;
        try{return checked((long)width*height*depth*4)<=bytes.LongLength;}catch(OverflowException){return false;}
    }
    internal static bool VolumeMips(byte[][] mips,int size)
    {
        if(mips==null||mips.Length==0||size<1)return false;
        int levels=1;for(int n=size;n>1;n>>=1)levels++;
        if(mips.Length>levels)return false;
        foreach(var mip in mips){if(!RgbaBytes(mip,size,size,size))return false;size=Math.Max(1,size/2);}
        return true;
    }
    internal static byte[][] CloneVolumeMips(byte[][] mips,int size)
    {
        var copy=new byte[mips.Length][];
        for(int i=0;i<mips.Length;i++)
        {
            int bytes=checked(size*size*size*4);copy[i]=new byte[bytes];Buffer.BlockCopy(mips[i],0,copy[i],0,bytes);size=Math.Max(1,size/2);
        }
        return copy;
    }
    internal static bool StructuredFloats(float[] values,int stride) => values!=null&&values.Length>0&&stride>0&&stride<=2048&&stride%4==0&&
        (long)values.Length*4<=int.MaxValue&&(long)values.Length*4%stride==0;

    internal static bool ColorFormat(int requested,out int dxgi,out int native)
    {
        switch(requested)
        {
            case 0: case 28: dxgi=28;native=0;return true;
            case 1: case 10: dxgi=10;native=1;return true;
            case 2: dxgi=2;native=6;return true;
            case 34: dxgi=34;native=3;return true;
            case 41: dxgi=41;native=2;return true;
            case 54: dxgi=54;native=8;return true;
            default: dxgi=native=0;return false;
        }
    }
}
