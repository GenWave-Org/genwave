namespace GenWave.Host.Voices;

/// <summary>
/// SEAM (SPEC F219.13) — who is speaking in a clip. A clip is in use when a persona's voice map or one of the station voice
/// settings gives its slug as the voice on an engine that clones. The answer names the users in plain words, for the 409 that
/// refuses a delete.
/// </summary>
public interface IVoiceClipUsageReader
{
    /// <summary>The names of everything using <paramref name="slug"/>: DJ names, then setting labels. Empty when the clip is free.</summary>
    Task<IReadOnlyList<string>> FindUsersAsync(string slug, CancellationToken ct);
}
