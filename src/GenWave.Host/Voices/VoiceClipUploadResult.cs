using GenWave.Core.Domain;

namespace GenWave.Host.Voices;

/// <summary>Outcome of <see cref="VoiceClipService.UploadAsync"/>.</summary>
public abstract record VoiceClipUploadResult
{
    private VoiceClipUploadResult() { }

    public sealed record Created(VoiceClip Clip) : VoiceClipUploadResult;

    /// <summary>The upload was refused; <paramref name="Reason"/> is plain words for the uploader.</summary>
    public sealed record Rejected(string Reason) : VoiceClipUploadResult;
}
