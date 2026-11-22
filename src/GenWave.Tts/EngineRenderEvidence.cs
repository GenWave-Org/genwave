namespace GenWave.Tts;

/// <summary>What the render path knows about one engine right now (SPEC F219.25, T768a).</summary>
/// <param name="InFlight">At least one speech render to the engine is running.</param>
/// <param name="LastSucceededAt">When the latest render that completed did so, if any.</param>
/// <param name="FailuresInARow">Failed or timed-out renders since the last success or the last time they were taken.</param>
/// <param name="Failure">The reason of the latest of those failures, if any.</param>
public sealed record EngineRenderEvidence(bool InFlight, DateTimeOffset? LastSucceededAt, int FailuresInARow, string? Failure)
{
    public static EngineRenderEvidence None { get; } = new(false, null, 0, null);

    /// <summary>True once enough renders failed in a row to call the engine down.</summary>
    public bool IsDown => FailuresInARow >= EngineRenderActivity.RedAfterFailures;

    /// <summary>True when a render finished successfully no longer than <paramref name="window"/> before <paramref name="now"/>.</summary>
    public bool SucceededWithin(DateTimeOffset now, TimeSpan window) =>
        LastSucceededAt is { } at && now - at <= window;
}
