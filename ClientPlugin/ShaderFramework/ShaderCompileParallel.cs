using System;
using System.Threading.Tasks;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Shared TPL compile fan-out. Matches Keen's cache generator cap of 8.
/// Do not use ParallelTasks.Parallel — that is the gameplay worker overlay.
/// </summary>
static class ShaderCompileParallel
{
    public static readonly ParallelOptions Options = new()
    {
        MaxDegreeOfParallelism = Degree()
    };

    public static int Degree()
    {
        return Math.Max(1, Math.Min(8, Environment.ProcessorCount));
    }

    public static void For(int fromInclusive, int toExclusive, Action<int> body)
    {
        if (toExclusive - fromInclusive <= 1)
        {
            if (fromInclusive < toExclusive)
                body(fromInclusive);
            return;
        }

        Parallel.For(fromInclusive, toExclusive, Options, body);
    }
}
