namespace GenWave.Tts;

/// <summary>
/// The single Record point every resolved LLM call feeds (SPEC F139.1, F139.2, STORY-353, PLAN T330 —
/// review finding F2). Before this class existed, SIX call sites across two assemblies
/// (<see cref="LlmCopyWriter"/>'s own success and catch-all paths, <see cref="CrosstalkScriptWriter"/>'s
/// own Accept/Discard/catch-all paths, and <c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c>'s own
/// break-window abandon) each wrote the SAME <see cref="LlmCallRing.Record"/> call immediately followed
/// by the SAME <see cref="LlmCallCauseCounters.Record"/> call, in lockstep, with only a code comment at
/// each site enforcing that the (cause, model, kind) triple passed to both stayed the same. A mutation
/// deleting just the counters half of that pair left every ring-facing fact green (T330 review's own
/// finding: "a mutation deleting ALL counter feeds pass green") — this class makes that mutation
/// impossible to express: there is no longer a "just the counters half" to delete, only one Record call
/// that does both or neither.
///
/// <para>
/// ONE required dependency pair (mirrors <c>GenWave.Host.Crosstalk.CrosstalkStockPacing</c>'s own "the
/// ONE dependency" shape one project over) — <see cref="LlmCallRing"/> and <see cref="LlmCallCauseCounters"/>
/// stay separate singletons rather than merging into one class (see <see cref="LlmCallCauseCounters"/>'s
/// own remarks for why: <c>Story196_LlmCallInspector</c>'s F73.3 structural proof pins
/// <see cref="LlmCallRing"/>'s constructor to exactly one parameter as evidence it cannot persist
/// anything, so a second dependency there would break that proof for an unrelated reason). This class is
/// where the two independent observers of "a call resolved" reunite into the one call site every writer
/// actually wants.
/// </para>
///
/// <para>
/// <b>Degradation mode stays a parameter, deliberately NOT a dependency here.</b> Every caller already
/// reads <see cref="IDegradationModeReader.CurrentMode"/> itself, ONCE, at the moment its own generation
/// attempt STARTS (<see cref="LlmCopyWriter.RequestCleanedCompletionAsync"/>'s own remarks: "mode is read
/// fresh right here... reading it uniformly for every path keeps this the one recording point instead of
/// two") — a call can run for real wall-clock seconds (up to <c>Llm:TimeoutSeconds</c>) before it
/// resolves, and the ring entry is meant to reflect the mode active when the ATTEMPT started, not
/// whatever the mode happened to drift to by the time this class's own <see cref="Record"/> runs. Folding
/// <see cref="IDegradationModeReader"/> in as a dependency here and reading it fresh at record time would
/// silently relocate that read to the wrong instant for every existing caller — so <paramref name="mode"/>
/// stays threaded through exactly as it already was, each caller's own already-captured value.
/// </para>
/// </summary>
public sealed class LlmCallRecorder(LlmCallRing callRing, LlmCallCauseCounters causeCounters)
{
    /// <summary>
    /// Records one resolved call into both the ring and the rolling counters — the one call every
    /// former <see cref="LlmCallRing.Record"/>/<see cref="LlmCallCauseCounters.Record"/> pair collapses
    /// to. Parameters mirror <see cref="LlmCallRing.Record"/>'s own exactly; <paramref name="cause"/>/
    /// <paramref name="model"/>/<paramref name="kind"/> are the SAME triple both stores key on, passed
    /// here exactly once rather than twice.
    /// </summary>
    public void Record(
        string? personaName, string? promptSystem, string? promptUser, string? response, DateTimeOffset startedAt,
        long elapsedMs, LlmCallOutcome outcome, string? statusDetail, DegradationMode mode, LlmCallCause cause,
        string model, LlmCallKind kind = LlmCallKind.Copy)
    {
        callRing.Record(
            personaName, promptSystem, promptUser, response, startedAt, elapsedMs, outcome, statusDetail, mode,
            cause, model, kind);
        causeCounters.Record(cause, model, kind);
    }
}
