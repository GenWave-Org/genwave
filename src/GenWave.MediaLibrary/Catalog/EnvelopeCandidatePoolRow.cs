using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Catalog;

/// <summary>
/// Dapper projection for <see cref="MediaRepository.GetEnvelopeCandidatePoolAsync"/> (SPEC F82.2,
/// STORY-213) — <see cref="RotationCandidateRow"/>'s exact columns plus the ones the pool query adds
/// for the ranker: <see cref="Energy"/> (the LUFS-percentile <c>library.media.energy</c> column) and,
/// as of SPEC F151.1 (STORY-372, PLAN T359), <see cref="Nudge"/>/<see cref="PlayCount"/> (the
/// <c>library.media_rotation</c> left-join columns, <c>coalesce(...,0)</c>'d at the query so a
/// never-aired/no-ledger-row track reads <c>0</c>/<c>0</c> rather than <see langword="null"/>).
/// <c>Moods</c> (the <c>library.media.moods</c> array column) rides the inherited
/// <see cref="MediaRow.Moods"/> property (SPEC F86.8) rather than a second declaration of its own.
/// </summary>
sealed class EnvelopeCandidatePoolRow : RotationCandidateRow
{
    public double? Energy { get; set; }

    public double Nudge { get; set; }

    public int PlayCount { get; set; }

    public EnvelopeCandidateRow ToPoolCandidate(ILogger logger) =>
        new(ToReference(logger), Energy, Moods ?? [], RepeatedRecent, RepeatedArtist)
        {
            Nudge = Nudge,
            PlayCount = PlayCount,
        };
}
