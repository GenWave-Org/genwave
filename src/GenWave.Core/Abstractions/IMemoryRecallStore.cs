using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F218.19, STORY-539) — the read side of <c>station.persona_memory</c> that the never-mention
/// list needs. The window is a duration, measured by the store's one clock (the database's), never by a
/// caller-computed cutoff.
/// </summary>
public interface IMemoryRecallStore
{
    /// <summary>Claim texts of any row forgotten within <paramref name="within"/>, newest forgotten first, at most <paramref name="take"/>.</summary>
    Task<IReadOnlyList<string>> RecentlyForgottenAsync(long personaId, TimeSpan within, int take, CancellationToken ct);
}
