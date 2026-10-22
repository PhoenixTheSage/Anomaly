using System;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    static void ColorHistoryContracts()
    {
        GuestBridge.Ready=true;GuestBridge.SupportedPacketFlags|=32768;
        var frame=new byte[344];
        void U(int at,uint value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,frame,at,4);
        void F(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,frame,at,4);
        U(16,123);U(20,1);F(264,1);F(284,1);F(304,1);F(324,1);F(332,95);F(336,73);
        GuestBridge.FrameOverride=frame;GuestBridge.FrameInputs=new object[]{new double[3],new float[]{1,1,1,1,0,-1,0,1}};
        SceneView.Width=48;SceneView.Height=37;GuestFrame.ResetDevice();Require(GuestFrame.Capture(),"history sizing frame unavailable");
        var history=new GuestProgram{Id="anomaly.owned.HistoryCopy",Mark=GuestMark.AfterUpscale,Compose="PublishOnly",Scale=1};
        GuestBridgeClient.PassSize(history,out int width,out int height);
        Require(width==48&&height==37,"Owned historyColor must use raster dimensions, matching DX11 EnsureHistoryTarget/ResolutionI");
        var ordinary=new GuestProgram{Id="fixture.late.half",Mark=GuestMark.AfterUpscale,Compose="PublishOnly",Scale=.5f};
        GuestBridgeClient.PassSize(ordinary,out width,out height);Require(width==47&&height==36,"ordinary AfterUpscale half size must floor output dimensions independently of DRS rounding");
        ordinary.Compose="Replace";ordinary.Scale=.25f;GuestBridgeClient.PassSize(ordinary,out width,out height);Require(width==95&&height==73,"AfterUpscale Replace must keep full output dimensions");
        Console.WriteLine("PASS: reserved HDR color history uses raster size; ordinary AfterUpscale uses output size and DX11 scaling");
    }
}
