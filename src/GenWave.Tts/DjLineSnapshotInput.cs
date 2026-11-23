using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// What <see cref="LlmCopyWriter"/> already holds when it decides on one DJ-line attempt, handed to
/// <see cref="DjLineSnapshotSource"/> unchanged. The source does no reads of its own: the lists are the ones the
/// writer read inside its single-flight section, so the snapshot sees exactly what the legacy checks saw.
/// </summary>
/// <param name="Request">The segment request: kind, station name, track, crossing track.</param>
/// <param name="Attempt">1 for the first write, 2 for the retry.</param>
/// <param name="CleanedText">The attempt's text after clean-up; sound tags are stripped again by the source.</param>
/// <param name="PersonaId">The speaking persona.</param>
/// <param name="PersonaName">The speaking persona's name; also the line's speaker label.</param>
/// <param name="Card">The persona card; its raw quirk rule texts become card phrases (key phrases are the rule's job).</param>
/// <param name="FactBlock">The facts offered, callback facts included, one per line; null or blank means none.</param>
/// <param name="OwnerMessageCore">The owner's message core when the line carries one.</param>
/// <param name="Timezone">The station timezone id; <see cref="SegmentRequest.LocalNow"/> is already in it.</param>
/// <param name="NewestRows">The newest rows the writer read (<c>RecentAsync(personaId, 10)</c>), fallbacks included.</param>
/// <param name="TaglineWindowRows">The tagline-window rows (<c>ReadTaglineEntriesAsync</c>), fallbacks included.</param>
/// <param name="LastHourRows">The rows from the last hour, fallbacks included.</param>
/// <param name="Preview">True for a preview render: the writer's on-air flag (<c>spendQuirks</c>) is false, so legacy never ran its repeat check.</param>
/// <param name="Legacy">The legacy quirk + tagline result while it still runs (shadow); null otherwise.</param>
public sealed record DjLineSnapshotInput(
    SegmentRequest Request,
    int Attempt,
    string CleanedText,
    long PersonaId,
    string PersonaName,
    PersonaCard? Card,
    string? FactBlock,
    string? OwnerMessageCore,
    string Timezone,
    IReadOnlyList<UtteranceEntry> NewestRows,
    IReadOnlyList<UtteranceEntry> TaglineWindowRows,
    IReadOnlyList<UtteranceEntry> LastHourRows,
    LegacyCatchphraseResult? Legacy = null,
    bool Preview = false);
