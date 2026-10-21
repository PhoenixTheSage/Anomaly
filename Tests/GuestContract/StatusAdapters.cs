// The parser's optional status formatter references producer status. This
// CPU-only fixture supplies that display surface; no GPU producer is simulated.
namespace ClientPlugin.Backend
{
    static class GuestOccupancy { internal static string StatusLine => "not exercised"; }
    static class GuestPointShadows { internal static string Status => "not exercised"; }
}
namespace ClientPlugin.Buffers
{
    static class BufferCatalog
    {
        internal static object Active(string name) => null;
        internal static void Set(string name, object value) { }
    }
}
