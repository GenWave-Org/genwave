using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F219.12/F219.13, STORY-557, PLAN T756) — persistence for <c>station.voice_clip</c>: the reference clips a cloning
/// voice engine speaks in. Rows only; the audio files live in the clips folder and are the caller's to write and remove.
/// Physically in <c>GenWave.Core</c>, not the published Abstractions NuGet: no consumer outside this solution.
/// </summary>
public interface IVoiceClipStore
{
    /// <summary>Every clip, oldest first (then by slug).</summary>
    Task<IReadOnlyList<VoiceClip>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Inserts <paramref name="clip"/>. Returns the stored row, or null when the slug is already taken (the table's unique
    /// constraint is the arbiter, so two racing uploads can never share a slug).
    /// </summary>
    Task<VoiceClip?> InsertAsync(NewVoiceClip clip, CancellationToken ct);

    /// <summary>Deletes the clip with <paramref name="slug"/>; returns false when there was none.</summary>
    Task<bool> DeleteAsync(string slug, CancellationToken ct);
}
