using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>One clip as <c>GET /api/voices/clips</c> and the upload response show it. The slug is the clip's voice id on a cloning engine.</summary>
public sealed record VoiceClipDto(
    string Slug,
    string Label,
    int DurationMs,
    string Source,
    string? Credit,
    DateTime CreatedAt)
{
    internal static VoiceClipDto From(VoiceClip clip) =>
        new(clip.Slug, clip.Label, clip.DurationMs, clip.Source, clip.Credit, clip.CreatedAt);
}
