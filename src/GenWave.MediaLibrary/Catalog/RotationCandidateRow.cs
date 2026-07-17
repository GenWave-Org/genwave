using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Dapper projection for <see cref="MediaRepository.GetRotationCandidateAsync"/> (SPEC F41.1) — the
/// same flat columns as <see cref="MediaRow"/> plus the two preference-tier booleans, computed in the
/// same query (no C# artist state, F41.3) and mapped by <c>MatchNamesWithUnderscores</c> exactly like
/// the base columns.
/// </summary>
sealed class RotationCandidateRow : MediaRow
{
    /// <summary>Tier 1 (F41.3): the picked id was among <c>orderedRecentIds</c>.</summary>
    public bool RepeatedRecent { get; set; }

    /// <summary>Tier 2 (F41.3): the picked artist matched an artist among the last N recent entries.</summary>
    public bool RepeatedArtist { get; set; }

    public RotationCandidate ToCandidate(ILogger logger) => new(ToReference(logger), RepeatedRecent, RepeatedArtist);
}
