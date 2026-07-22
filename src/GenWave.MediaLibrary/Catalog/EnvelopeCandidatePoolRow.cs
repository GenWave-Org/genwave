using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Dapper projection for <see cref="MediaRepository.GetEnvelopeCandidatePoolAsync"/> (SPEC F82.2,
/// STORY-213) — <see cref="RotationCandidateRow"/>'s exact columns plus the two the pool query adds
/// for the ranker: <see cref="Energy"/> (the LUFS-percentile <c>library.media.energy</c> column) and
/// <see cref="Moods"/> (the <c>library.media.moods</c> array column).
/// </summary>
sealed class EnvelopeCandidatePoolRow : RotationCandidateRow
{
    public double? Energy { get; set; }
    public string[]? Moods { get; set; }

    public EnvelopeCandidateRow ToPoolCandidate(ILogger logger) =>
        new(ToReference(logger), Energy, Moods ?? [], RepeatedRecent, RepeatedArtist);
}
