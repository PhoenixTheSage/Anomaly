using System;
using System.Collections.Generic;
using VRageMath;
using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

public static class FullscreenPassRegistry
{
    public static ShaderProgramStatus GetProgramState(string id)
    {
        if (RenderHost.IsDx11)
        {
            Dx11Call.Try(Dx, "GetExecutionState", new object[] { id }, out var result);
            var state = result as object[];
            if (state == null) return new ShaderProgramStatus();
            return new ShaderProgramStatus { Registered = (bool)state[0], Compiled = (bool)state[1], Active = (bool)state[2],
                ProductionStatusSupported = true, LastProducedFrame = (uint)state[3], Executions = (ulong)state[4],
                ProducedThisFrame = (ulong)state[4] != 0 && (uint)state[3] == FrameTemporal.FrameIndex, Error = state[5] as string };
        }
        foreach (var program in GuestPassList.Snapshot())
            if (string.Equals(program.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                var native = program.Pipeline == 0 ? null : GuestBridgeClient.ExecutionStatus(program.Pipeline);
                var state = new ShaderProgramStatus { Registered = true, Compiled = program.PixelSpirv != null,
                    Active = program.Enabled && !program.ConflictDisabled && program.Pipeline != 0, Error = program.Error, ActiveGeneration = program.Generation,
                    ProductionStatusSupported = native != null && native.Length == 48 };
                if (state.ProductionStatusSupported)
                {
                    state.Executions = BitConverter.ToUInt64(native, 16); state.LastProducedFrame = BitConverter.ToUInt32(native, 40);
                    state.LastSubmissionCompleted = state.Executions != 0 && BitConverter.ToUInt64(native, 24) <= BitConverter.ToUInt64(native, 32);
                    state.ProducedThisFrame = state.Executions != 0 && GuestFrame.Open && state.LastProducedFrame == GuestFrame.U32(16);
                }
                return state;
            }
        return new ShaderProgramStatus();
    }
    public static bool IsPackSrvSlot(int slot) => slot == 7 || slot == 8 || slot == 9 || slot == 12 || slot == 13;
    public static string StatusLine =>
        RendererRoute.UseGuestImages ? GuestPassList.FullscreenStatus() : ForwardString("StatusLine");

    public static bool SetUniforms(string id, float[] values)
    {
        GuestPassList.SetUniforms(id, values);
        return RenderHost.IsDx11 ? ForwardBool("SetUniforms", id, values) : true;
    }

    public static bool RequestSrv(string programId, string catalogName, int slot = -1)
    {
        if (RendererRoute.UseGuestImages)
            return GuestPassList.RequestSrv(programId, catalogName, slot);
        if (RendererRoute.RefuseDx11Device("FullscreenPassRegistry.RequestSrv"))
            return false;
        return ForwardBool("RequestSrv", programId, catalogName, slot);
    }

    public static bool SetEnabled(string programId, bool enabled)
    {
        GuestPassList.SetEnabled(programId, enabled);
        // LoadAssets runs before DX11 exists. Hold the checkbox and apply it
        // after the json programs are created, or a default-on Replace stays live.
        if (RenderHost.HoldEnabled(programId, enabled))
            return true;
        return RenderHost.IsDx11 ? ForwardBool("SetEnabled", programId, enabled) : true;
    }

    public static bool SetScale(string programId, float scale)
    {
        if (RendererRoute.UseGuestImages)
            return GuestPassList.SetScale(programId, scale);
        if (RendererRoute.RefuseDx11Device("FullscreenPassRegistry.SetScale"))
            return false;
        return ForwardBool("SetScale", programId, scale);
    }

    public static bool SetVelocityDistanceScale(string programId, float metresPerAlphaUnit)
    {
        if(!RendererRoute.UseDx11Device)return GuestPassList.SetVelocityDistanceScale(programId,metresPerAlphaUnit);
        if (RendererRoute.RefuseDx11Device("FullscreenPassRegistry.SetVelocityDistanceScale"))
            return false;
        return ForwardBool("SetVelocityDistanceScale", programId, metresPerAlphaUnit);
    }

    public static void DrawFullscreen(object rc, int width, int height)
    {
        if (RendererRoute.RefuseDx11Device("FullscreenPassRegistry.DrawFullscreen"))
            return;
        Dx11Call.Call(Dx, "DrawFullscreen", rc, width, height);
    }

    public static bool TryGetOutputSize(string programId, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (RendererRoute.UseGuestImages)
        {
            var programs = GuestPassList.Snapshot();
            for (int i = 0; i < programs.Count; i++)
            {
                if (!string.Equals(programs[i].Id, programId, StringComparison.OrdinalIgnoreCase))
                    continue;
                GuestBridgeClient.PassSize(programs[i],out width,out height);
                return true;
            }
            return false;
        }
        if (RendererRoute.RefuseDx11Device("FullscreenPassRegistry.TryGetOutputSize"))
            return false;
        object[] args = { programId, 0, 0 };
        if (!Dx11Call.Try(Dx, "TryGetOutputSize", args, out var result) || result is not true)
            return false;
        width = args[1] is int w ? w : 0;
        height = args[2] is int h ? h : 0;
        return true;
    }

    public static bool TryGetProgramStatus(string programId, out string status)
    {
        if (RenderHost.IsDx11)
        {
            object[] args = { programId, null };
            if (Dx11Call.Try(Dx, "TryGetProgramStatus", args, out var result) && result is true)
            {
                status = args[1] as string;
                return true;
            }
        }

        return GuestPassList.TryGetProgramStatus(programId, out status);
    }

    public static void RequestLitMips(int mipLevels = 5) => ClientPlugin.Buffers.BufferCatalog.RequestLitMips(mipLevels);
    public static void RequestPointShadows(int maxLights = 4, int faceResolution = 128) =>
        ClientPlugin.Buffers.BufferCatalog.RequestPointShadows(maxLights, faceResolution);
    public static void RequestWorldBoxes(bool enabled = true) => ClientPlugin.Buffers.BufferCatalog.RequestWorldBoxes(enabled);
    public static void RequestOccupancy(bool enabled = true) => ClientPlugin.Buffers.BufferCatalog.RequestOccupancy(enabled);

    static void Device(string method, params object[] args)
    {
        if (RendererRoute.RefuseDx11Device("FullscreenPassRegistry." + method))
            return;
        Dx11Call.Call(Dx, method, args);
    }

    const string Dx = "ClientPlugin.Shaders.FullscreenPassRegistry";
    static bool ForwardBool(string method, params object[] args) =>
        Dx11Call.Try(Dx, method, args, out var result) && result is true;
    static string ForwardString(string property) => Dx11Call.Get(Dx, property) as string ?? "";
}

public static class ShaderPackRegistry
{
    public static NativeStageGenerationState GetNativeStageState(string stage)=>GuestPostprocessStages.Query(stage);
    public static OwnedStageGenerationState GetOwnedStageState(string stage)=>GuestNamedStages.State(stage);
    public static ShaderApiCapabilities GetCapabilities()=>ShaderApiCapabilities.Query();
    public static string StatusLine =>
        RenderHost.IsDx11 ? Dx11Call.Get(Dx, "StatusLine") as string ?? "" : GuestPassList.PackStatus();
    public static string LastError => Dx11Call.Get(Dx, "LastError") as string;

    public static void Register(string id, string root) => RenderHost.QueueRegister(id, root);
    public static void Unregister(string id) => RenderHost.QueueUnregister(id);

    public static bool TryRegister(string id, string root, string requiredFullscreenProgramId, out string error)
    {
        error = null;
        try { RenderHost.QueueRegister(id, root, requiredFullscreenProgramId); }
        catch (Exception failure) { error = failure.Message; return false; }
        if (!RenderHost.IsDx11)
            return true;
        object[] args = { id, GuestPassList.SourceFor(id)?.Root??root, requiredFullscreenProgramId, null };
        if (!Dx11Call.Try(Dx, "TryRegister", args, out var result))
            return false;
        error = args[3] as string;
        return result is true;
    }

    const string Dx = "ClientPlugin.Shaders.ShaderPackRegistry";
}

public static class OwnedPassRegistry
{
    public static void RegisterPortable(string id,string slot,int priority,int temporalPolicy,Action<object> draw,int phase=1)
        =>PortablePassRegistry.Register(id,slot,priority,temporalPolicy,draw,phase);
    public static string PortableError(string id)=>PortablePassRegistry.Error(id);
    public static string StatusLine => GuestColor.Open ? GuestColor.Status : Get("StatusLine");
    public static string ColorStatusLine => GuestColor.Open ? GuestColor.ColorStatus : Get("ColorStatusLine");
    public static bool HasDisplayTenant => GuestColor.Open ? GuestColor.HasDisplay : Flag("HasDisplayTenant");
    public static bool HasUpscaleConsumer => GuestColor.Open ? GuestColor.HasUpscale : Flag("HasUpscaleConsumer");
    public static string UpscaleConsumerId => GuestColor.Open ? GuestColor.UpscaleId : Get("UpscaleConsumerId");
    public static bool HasUpscaledColor => GuestColor.Open ? GuestColor.Notified : Flag("HasUpscaledColor");
    public static bool WasUpscaleNotified => GuestColor.Open ? GuestColor.Notified : Flag("WasUpscaleNotified");
    public static bool ClaimUpscale(string id) => GuestColor.Open ? GuestColor.Claim(id) : Call("ClaimUpscale", id) is true;
    public static void ReleaseUpscale(string id)
    {
        if (GuestColor.Open) { GuestColor.Release(id); return; }
        Device("ReleaseUpscale", id);
    }
    public static bool HasSlot(string slot) => Call("HasSlot", slot) is true;

    public static void Register(string id, string slot, int priority, int temporalPolicy, Action<object> draw) =>
        Register(id, slot, priority, temporalPolicy, draw, 1);

    public static void Register(string id, string slot, int priority, int temporalPolicy, Action<object> draw, int phase)
    {
        // Phase 1 is AfterFullscreen, matching the DX11 5-arg Register.
        if (RenderHost.HoldOwnedPass(id, slot, priority, temporalPolicy, draw, phase))
            return;
        Device("Register", id, slot, priority, temporalPolicy, draw, phase);
    }

    public static void Unregister(string id)
    {
        PortablePassRegistry.Unregister(id);
        if (RenderHost.DropHeldOwnedPass(id))
            return;
        Device("Unregister", id);
    }
    public static void NotifyUpscaleComplete()
    {
        if (GuestColor.Open) { GuestColor.Notify(); return; }
        Device("NotifyUpscaleComplete");
    }
    public static void NotifyUpscaleComplete(object renderContextOrColor)
    {
        if (GuestColor.Open) { GuestColor.Notify(); return; }
        Device("NotifyUpscaleComplete", renderContextOrColor);
    }
    public static void NotifyUpscaleComplete(object renderContext, object color)
    {
        if (GuestColor.Open) { GuestColor.Notify(); return; }
        Device("NotifyUpscaleComplete", renderContext, color);
    }

    const string Dx = "ClientPlugin.Shaders.OwnedPassRegistry";
    static bool Closed() => RendererRoute.RefuseDx11Device("OwnedPassRegistry");
    static string Get(string property) => Closed() ? "" : Dx11Call.Get(Dx, property) as string ?? "";
    static bool Flag(string property) => !Closed() && Dx11Call.Get(Dx, property) is true;
    static object Call(string method, params object[] args)
    {
        if (Closed())
            return null;
        Dx11Call.Try(Dx, method, args, out var result);
        return result;
    }

    static void Device(string method, params object[] args)
    {
        if (Closed())
            return;
        Dx11Call.Call(Dx, method, args);
    }
}

public static class CelestialBackgroundRegistry
{
    public static string StatusLine => Closed() ? "" : Dx11Call.Get(Dx, "StatusLine") as string ?? "";
    public static void SetNativeSunGlare(string id, bool enabled)
    {
        if (RendererRoute.UseDx11Device) { Device("SetNativeSunGlare", id, enabled); return; }
        GuestColor.SetGlare(id, enabled);
    }
    public static bool Register(string id, string shaderFile)
    {
        if (RendererRoute.UseDx11Device)
            return Dx11Call.Try(Dx, "Register", new object[] { id, shaderFile }, out var result) && result is true;
        if (!RendererRoute.UseGuestImages || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(shaderFile))
            return false;
        GuestPassList.AddProgram(id, "AfterLighting", shaderFile, "Replace");
        GuestPassList.SetFlags(id, GuestBridgeClient.DepthTest, 0);
        return true;
    }
    public static void Unregister(string id)
    {
        if (RendererRoute.UseDx11Device) { Device("Unregister", id); return; }
        GuestPassList.SetEnabled(id, false);
    }
    public static void SetEnabled(string id, bool enabled)
    {
        if (RendererRoute.UseDx11Device) { Device("SetEnabled", id, enabled); return; }
        GuestPassList.SetEnabled(id, enabled);
    }
    public static void Retry(string id) => Device("Retry", id);
    public static void SetUniforms(string id, float[] values)
    {
        if (RendererRoute.UseDx11Device) { Device("SetUniforms", id, values); return; }
        GuestPassList.SetUniforms(id, values);
    }
    public static void SetData(string id, float[] values)
    {
        if (RendererRoute.UseDx11Device) { Device("SetData", id, values); return; }
        GuestColor.SetCelestialData(id, values);
    }
    public static void SetArt(string id, int width, int height, int slices, byte[] rgba)
    {
        if (RendererRoute.UseDx11Device) { Device("SetArt", id, width, height, slices, rgba); return; }
        GuestColor.SetCelestialArt(id, width, height, slices, rgba);
    }
    public static void SetArt(string id, byte[] rgba)
    {
        if (RendererRoute.UseDx11Device) { Device("SetArt", id, rgba); return; }
        GuestColor.SetCelestialArt(id, 1, 1, 1, rgba);
    }

    const string Dx = "ClientPlugin.Shaders.CelestialBackgroundRegistry";
    static bool Closed() => RendererRoute.RefuseDx11Device("CelestialBackgroundRegistry");
    static void Device(string method, params object[] args)
    {
        if (Closed())
            return;
        Dx11Call.Call(Dx, method, args);
    }
}

public static class FrameTemporal
{
    public static uint FrameIndex => GuestFrame.Open ? GuestFrame.U32(16) : Value<uint>("FrameIndex");
    public static float JitterX => GuestFrame.Open ? GuestFrame.F32(24) : Value<float>("JitterX");
    public static float JitterY => GuestFrame.Open ? GuestFrame.F32(28) : Value<float>("JitterY");
    public static bool HistoryValid => GuestFrame.Open ? GuestFrame.History : !Closed() && Dx11Call.Get(Dx, "HistoryValid") is true;
    public static int RenderWidth => GuestFrame.Open ? (int)GuestFrame.U32(8) : Value<int>("RenderWidth");
    public static int RenderHeight => GuestFrame.Open ? (int)GuestFrame.U32(12) : Value<int>("RenderHeight");
    public static Matrix UnjitteredViewProj => GuestFrame.Open ? GuestFrame.MatrixAt(136) : Value<Matrix>("UnjitteredViewProj");
    public static Matrix PrevViewProj => GuestFrame.Open ? GuestFrame.MatrixAt(200) : Value<Matrix>("PrevViewProj");
    public static Matrix CameraToWorld => GuestFrame.Open ? GuestFrame.MatrixAt(264) : Value<Matrix>("CameraToWorld");
    public static Vector2 ProjScale => GuestFrame.Open ? new Vector2(GuestFrame.F32(60), GuestFrame.F32(64)) : Value<Vector2>("ProjScale");
    public static float SafetyScale => GuestFrame.Open ? GuestFrame.F32(32) : Value<float>("SafetyScale");
    public static Vector3 CameraDelta => GuestFrame.Open ? GuestFrame.Vec3(120) : Value<Vector3>("CameraDelta");
    public static Vector3 SunColor => GuestFrame.Open ? GuestFrame.Vec3(72) : Value<Vector3>("SunColor");
    public static Vector3 SunToward => GuestFrame.Open ? GuestFrame.Vec3(88) : Value<Vector3>("SunToward");
    public static float SunDiffuse => GuestFrame.Open ? GuestFrame.F32(36) : Value<float>("SunDiffuse");
    public static float SkyLuma => GuestFrame.Open ? GuestFrame.F32(40) : Value<float>("SkyLuma");
    public static Vector3 SkyAmbient => GuestFrame.Open ? GuestFrame.Vec3(104) : Value<Vector3>("SkyAmbient");
    public static float PlanetAirTop => GuestFrame.Open ? GuestFrame.F32(44) : Value<float>("PlanetAirTop");
    public static float VisualAtmoCeil => GuestFrame.Open ? GuestFrame.F32(48) : Value<float>("VisualAtmoCeil");
    public static float FarClip => GuestFrame.Open ? GuestFrame.F32(328) : Value<float>("FarClip");
    public static int OutputWidth => GuestFrame.Open ? (int)GuestFrame.F32(332) : Value<int>("OutputWidth");
    public static int OutputHeight => GuestFrame.Open ? (int)GuestFrame.F32(336) : Value<int>("OutputHeight");
    public static void InvalidateHistory()
    {
        if (GuestFrame.Open)
        {
            GuestFrame.Invalidate();
            return;
        }
        if (Closed())
            return;
        Dx11Call.Call(Dx, "InvalidateHistory");
    }

    const string Dx = "ClientPlugin.Shaders.FrameTemporal";
    static bool Closed() => RendererRoute.RefuseDx11Device("FrameTemporal");
    static T Value<T>(string name) where T : struct => Closed() ? default : Dx11Call.Get(Dx, name) is T value ? value : default;
}

/// <summary>
/// Renderer services the DX11 device already gave packs: far clip, a display-sized
/// upscale, an HDR swapchain, and one extra present. Bits: 1 HDR, 2 frame
/// generation, 4 temporal upscale. Vendor SDKs stay in the pack on DX11.
/// </summary>
public static class DisplayServices
{
    public const int Hdr = 1;
    public const int FrameGeneration = 2;
    public const int TemporalUpscale = 4;

    public static void Request(int flags)
    {
        if (!RendererRoute.UseGuestImages || flags == 0)
            return;
        GuestBridgeClient.SetDisplayFlags((flags << 16) | flags);
    }

    public static void Release(int flags)
    {
        if (!RendererRoute.UseGuestImages || flags == 0)
            return;
        GuestBridgeClient.SetDisplayFlags(flags << 16);
    }

    public static void SetFarClip(float farPlane)
    {
        if (!RendererRoute.UseGuestImages || farPlane < 1f)
            return;
        GuestBridgeClient.SetViewClip(farPlane);
    }
}

public static class GBufferTargetRegistry
{
    public static bool Request(string name, string format, string file = null)
    {
        bool admitted = GuestPassList.RequestAttachment(name, format, file);
        if (RendererRoute.UseDx11Device)
            Dx11Call.Try("ClientPlugin.Shaders.GBufferAttachments", "Request", new object[] { name, format, "GBuffer" }, out _);
        return admitted;
    }
}

/// <summary>Shared stage reservations. StatusLine distinguishes stored Vulkan requests from native execution.</summary>
public static class ShaderBindRegistry
{
    public static NativeStageBindingStatus GetNativeState()=>NativeStageBindingStatus.Query();
    public const int LightingVelocitySlot=5,LightingAttachBase=6,LightingCbSlot=6,PostVelocitySlot=5,PostCbSlot=6,AtmosphereVelocitySlot=6,AtmosphereCbSlot=6,AtmosphereExtraBase=7,FirstExtraSrv=5,LastExtraSrv=9;
    public static string StatusLine=>!RenderHost.Decided?"Renderer pending; stage binding reservations retained":RenderHost.IsDx11?Dx11Call.Get("ClientPlugin.Shaders.ShaderBindRegistry","StatusLine") as string??"none":GuestStageBindings.Status;
    public static void RequestSrv(string stage,string catalogName,int slot=-1)
    {
        if(!GuestStageBindings.Model.Add(stage,catalogName,slot))return;
        if(RenderHost.IsDx11)Dx11Call.Try("ClientPlugin.Shaders.ShaderBindRegistry","RequestSrv",new object[]{stage,catalogName,slot},out _);
    }
}
