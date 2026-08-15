namespace GenWave.Tts;

using GenWave.Core.Domain;
using LoudnessMeasurement = GenWave.Core.Domain.Loudness;

/// <summary>
/// Outcome of <see cref="CrosstalkAssembler.AssembleAsync"/> (SPEC F127.5, F127.6, STORY-327) —
/// mirrors <see cref="CrosstalkWriteResult"/>'s own closed-hierarchy shape one stage upstream (an
/// assembled exchange always carries a real, measured asset; a discard always carries a reason,
/// never both, never neither). There is no partial/single-voice case by design (F127.5 — "both
/// voices or nobody"): any line failing to render, or the finished mix running past the SPEC F127.6
/// ceiling, collapses to <see cref="Discarded"/>.
/// </summary>
public abstract record CrosstalkAssemblyResult
{
    CrosstalkAssemblyResult() { }

    /// <summary>
    /// A fully assembled, measured exchange, ready to cache and vend like any other segment (a
    /// LATER task's concern — <see cref="Path"/>/<see cref="Loudness"/>/<see cref="Cue"/>/
    /// <see cref="DurationMs"/> are exactly the measure shape <c>TtsSegmentSource</c> already
    /// produces for an ordinary render, so a future caller composes a played segment from this the
    /// same way).
    /// </summary>
    /// <param name="Path">Absolute path to the single mixed audio asset under <c>Tts:CacheRoot</c>.</param>
    /// <param name="Loudness">Integrated loudness/true-peak of the ASSEMBLED asset (SPEC F127.6) —
    /// measured once, after mixing, never summed from the per-line renders.</param>
    /// <param name="Cue">Silence-trimmed cue points of the assembled asset, when detectable — null
    /// on the same terms <c>TtsSegmentSource</c>'s own cue analysis already tolerates (never gates
    /// readiness).</param>
    /// <param name="DurationMs">The assembled asset's duration, derived from <see cref="Cue"/>'s own
    /// <c>CueOutSec</c> — the SAME house shape <c>TtsSegmentSource</c>/<c>SafeSegmentAuthor</c>
    /// already use (<c>SafeSegmentAuthor.BuildInsert</c>'s own remarks), not a dedicated ffprobe
    /// read: cue analysis already ran to produce <see cref="Cue"/>, so this is free. Null exactly
    /// when <see cref="Cue"/> is null (cue analysis never gates readiness — see its own remarks) —
    /// never the ffprobe container-duration measurement <c>CrosstalkAssembler.AssembleAsync</c>
    /// takes for its own SPEC F127.6 ceiling check, which is a strictly internal gate, not a value
    /// this result ever carries.</param>
    public sealed record Assembled(string Path, LoudnessMeasurement Loudness, CuePoints? Cue, int? DurationMs) : CrosstalkAssemblyResult;

    /// <summary>
    /// No exchange was produced. <see cref="Reason"/> is the SAME text logged at Information (never
    /// a WARN — mirrors <see cref="CrosstalkWriteResult.Discarded"/>'s own posture: a discard here is
    /// discipline, not an outage) — one string, one source of truth for "why did this exchange never
    /// air".
    /// </summary>
    public sealed record Discarded(string Reason) : CrosstalkAssemblyResult;
}
