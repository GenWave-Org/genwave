namespace GenWave.Host.Voices;

/// <summary>Outcome of <see cref="VoiceClipNormalizer.NormalizeAsync"/>.</summary>
public abstract record VoiceClipNormalizeResult
{
    private VoiceClipNormalizeResult() { }

    /// <summary>The normalized file was written; <paramref name="DurationMs"/> is its measured length.</summary>
    public sealed record Normalized(int DurationMs) : VoiceClipNormalizeResult;

    /// <summary>The clip was refused; <paramref name="Reason"/> is plain words for the person who uploaded it.</summary>
    public sealed record Rejected(string Reason) : VoiceClipNormalizeResult;
}
