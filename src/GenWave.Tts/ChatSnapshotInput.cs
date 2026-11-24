using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// What <see cref="CrosstalkScriptWriter"/> already holds when it decides on one exchange attempt, handed to
/// <see cref="ChatSnapshotSource"/> unchanged. The source does no reads of its own: the row lists are the ones the
/// writer read (<c>RecentAsync(personaId, 10)</c> per side, fallbacks included), so the snapshot sees exactly what the
/// legacy checks saw. A side without a persona id has no rows to pass.
/// </summary>
/// <param name="Request">The exchange request: both cards and persona ids, station name, station-local now.</param>
/// <param name="Attempt">1 for the first write, 2 for the re-ask.</param>
/// <param name="Script">The parsed script for this attempt; sound tags are stripped by the source.</param>
/// <param name="Timezone">The station timezone id. The chat request carries none, so the caller supplies it.</param>
/// <param name="DurationTargetSeconds">The live <c>Crosstalk:DurationTargetSeconds</c> as the writer read it.</param>
/// <param name="MaxLineChars">The per-line budget (<c>Llm:MaxCopyChars</c>) the parser enforced.</param>
/// <param name="HostRows">The host's newest rows (<c>RecentAsync(hostId, 10)</c>), fallbacks included.</param>
/// <param name="NeighborRows">The neighbor's newest rows (<c>RecentAsync(neighborId, 10)</c>), fallbacks included.</param>
public sealed record ChatSnapshotInput(
    CrosstalkExchangeRequest Request,
    int Attempt,
    CrosstalkAiredScript Script,
    string Timezone,
    int DurationTargetSeconds,
    int MaxLineChars,
    IReadOnlyList<UtteranceEntry> HostRows,
    IReadOnlyList<UtteranceEntry> NeighborRows);
