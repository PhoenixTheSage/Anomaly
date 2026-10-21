namespace ClientPlugin.Shaders;

/// <summary>Registration, compilation and recording are separate observations.</summary>
public sealed class ShaderProgramStatus
{
    public bool Registered { get; internal set; }
    public bool Compiled { get; internal set; }
    public bool Active { get; internal set; }
    public bool ProductionStatusSupported { get; internal set; }
    public bool ProducedThisFrame { get; internal set; }
    public uint LastProducedFrame { get; internal set; } = uint.MaxValue;
    public ulong Executions { get; internal set; }
    public bool LastSubmissionCompleted { get; internal set; }
    public string Error { get; internal set; }
}
