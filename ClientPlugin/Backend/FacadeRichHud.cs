using System;
using System.Collections.Generic;
using ClientPlugin.Backend;

namespace ClientPlugin.RichHud;

public static class TerminalConfigRegistry
{
    public static string LastError => Dx11Call.Get(Dx, "LastError") as string;
    public static string StatusLine => Dx11Call.Get(Dx, "StatusLine") as string ?? "";
    public static object RequestPage(string title) => Call("RequestPage", title);
    public static object RequestFolderPage(string folderTitle, string pageTitle) => Call("RequestFolderPage", folderTitle, pageTitle);
    public static object RequestFolderPage(string pageTitle, IReadOnlyList<string> folders) => Call("RequestFolderPage", pageTitle, folders);
    public static bool UnregisterPage(string title) => Call("UnregisterPage", title) is true;

    const string Dx = "ClientPlugin.RichHud.TerminalConfigRegistry";
    static object Call(string method, params object[] args)
    {
        Dx11Call.Try(Dx, method, args, out var result);
        return result;
    }
}

public static class HudOverlayRegistry
{
    public static string StatusLine => Dx11Call.Get(Dx, "StatusLine") as string ?? "";
    public static bool Register(string id, Func<string> get) =>
        Dx11Call.Try(Dx, "Register", new object[] { id, get }, out var result) && result is true;
    public static bool Unregister(string id) =>
        Dx11Call.Try(Dx, "Unregister", new object[] { id }, out var result) && result is true;

    const string Dx = "ClientPlugin.RichHud.HudOverlayRegistry";
}
