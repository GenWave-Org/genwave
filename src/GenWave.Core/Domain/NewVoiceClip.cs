namespace GenWave.Core.Domain;

/// <summary>What an insert into <c>station.voice_clip</c> needs; the id and created time are the database's.</summary>
public sealed record NewVoiceClip(
    string Slug,
    string Label,
    string FileName,
    int DurationMs,
    string Source,
    string? Credit);
