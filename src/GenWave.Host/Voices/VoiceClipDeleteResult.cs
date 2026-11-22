namespace GenWave.Host.Voices;

/// <summary>Outcome of <see cref="VoiceClipService.DeleteAsync"/>.</summary>
public abstract record VoiceClipDeleteResult
{
    private VoiceClipDeleteResult() { }

    public sealed record Deleted : VoiceClipDeleteResult;

    public sealed record NotFound : VoiceClipDeleteResult;

    /// <summary>A persona or setting still speaks in the clip; <paramref name="Users"/> names them.</summary>
    public sealed record InUse(IReadOnlyList<string> Users) : VoiceClipDeleteResult;
}
