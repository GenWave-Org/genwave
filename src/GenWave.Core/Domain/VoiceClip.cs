namespace GenWave.Core.Domain;

/// <summary>
/// One reference clip a cloning voice engine can speak in (SPEC F219.12, <c>station.voice_clip</c>). The clip's voice id on a
/// cloning engine IS <see cref="Slug"/> and its audio is <c>&lt;slug&gt;.wav</c> in the clips folder (F219.28), so
/// <see cref="FileName"/> is always that relative name, never a path. <see cref="Source"/> is
/// <see cref="VoiceClipSources.Starter"/> or <see cref="VoiceClipSources.Upload"/>; <see cref="Credit"/> is null for uploads.
/// </summary>
public sealed record VoiceClip(
    long Id,
    string Slug,
    string Label,
    string FileName,
    int DurationMs,
    string Source,
    string? Credit,
    DateTime CreatedAt);
