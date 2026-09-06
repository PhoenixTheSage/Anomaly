namespace ClientPlugin;

/// <summary>
/// Developer-only capture points for tracing the GBuffer velocity target through
/// Keen's deferred render scheduler. Only the selected point is copied.
/// </summary>
public enum Target3Checkpoint
{
    Live = 0,
    AfterClear = 1,
    AfterOldGeometry = 2,
    AfterStage2 = 3,
    AfterDecals = 4,
    AfterResolver = 5,
    AfterTransparent = 6,
    BeforeGBufferDone = 7,
    SchedulerEnd = 8
}
