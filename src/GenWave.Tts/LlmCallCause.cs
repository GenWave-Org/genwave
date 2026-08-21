namespace GenWave.Tts;

/// <summary>
/// The F139 cause taxonomy (SPEC F139.1, STORY-353, PLAN T330) — WHY an LLM call resolved the way it
/// did, stamped onto every <see cref="LlmCallRecord"/> alongside its existing, coarser
/// <see cref="LlmCallOutcome"/> (which stays exactly as it was — F139.1's own "nothing else about F73
/// changes"). Where <see cref="LlmCallOutcome"/> answers "did the ring show usable text or not",
/// <see cref="LlmCallCause"/> answers "why is the red tile red" (SPEC F139.2, F139.4) — the taxonomy a
/// dominant-cause admin surface (a LATER task, T334) groups by, alongside model and
/// <see cref="LlmCallKind"/>.
///
/// <para>
/// <b>Resolution-point map (PLAN T330).</b> <see cref="LlmCopyWriter.RequestCleanedCompletionAsync"/>:
/// an exact-fit or salvaged-trim <c>CleanCopy</c> result is <see cref="Success"/>; a full reject is
/// <see cref="OverLength"/> when a candidate existed but none survived the cap, or
/// <see cref="EmptyCompletion"/> when hygiene left nothing at all
/// (<see cref="LlmCopyCleanupResult.Rejected.WasOverLength"/> is the source of truth, decided once at
/// <see cref="LlmCopyWriter.CleanCopy"/> — never re-derived from any later string). Its own catch-all
/// (<see cref="LlmCopyWriter.ClassifyForRing"/>, shared with <see cref="CrosstalkScriptWriter"/>) maps
/// our own <c>Llm:TimeoutSeconds</c> budget elapsing to <see cref="Timeout"/> and everything else
/// (non-2xx, connect failure, malformed endpoint URI, bad JSON) to <see cref="ConnectionFailure"/> —
/// PLAN T334's own doc note: that catch-all has no finer split between "a response arrived but was
/// non-2xx" and "no response ever arrived at all" either; <see cref="LlmCallRecord.StatusDetail"/>
/// (the HTTP status or exception type name) is what carries that finer distinction, for whichever
/// LATER task wants to split it out of <see cref="ConnectionFailure"/> rather than this enum growing a
/// ninth value for it today.
/// </para>
///
/// <para>
/// <see cref="CrosstalkScriptWriter"/>/<see cref="CrosstalkScriptParser"/>: an accepted script is
/// <see cref="Success"/>; a <c>finish_reason: length</c> truncation, a per-line budget overrun, or an
/// over-target duration estimate are all <see cref="OverLength"/> (the reply came back but did not
/// fit — this fold is unchanged by the amendment below). ⚠️ <b>Amended (T330 review round 1,
/// 2026-08-20 — the F135.5 precedent, SPEC F139.1):</b> every OTHER parse-shape reject — a line count
/// outside <see cref="CrosstalkScriptParser.MinLines"/>-<see cref="CrosstalkScriptParser.MaxLines"/>
/// (in EITHER direction: too few is genuinely empty-ish, but too MANY is the reviewer's own exhibit —
/// a reply carrying MORE content than the shape allows is not "empty" by any honest reading), an
/// unrecognized speaker tag, a missing HOST or NEIGHBOR turn, or broken speaker alternation — is
/// <see cref="MalformedResponse"/>, not <see cref="EmptyCompletion"/>: the reply came back and had
/// CONTENT, it just never took the required shape. Folding these into EmptyCompletion sent the
/// operator to the wrong levers (endpoint, max_tokens) when the right answer is "this model can't
/// follow the output format", and corrupted the F138.7 model-floor signal for which malformed-shape is
/// the single most model-discriminating cause. The one parser reject that STAYS
/// <see cref="EmptyCompletion"/>: a line whose tag matched correctly but whose text was empty after
/// hygiene cleanup — the SHAPE was fine, only the content was missing, the same "nothing usable
/// resulted" story <see cref="LlmCopyWriter"/>'s own empty-hygiene case tells. <see cref="CanceledByWindow"/>
/// is stamped by <c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c> alone, never by
/// <see cref="CrosstalkScriptWriter"/> itself — that writer's own <see cref="OperationCanceledException"/>
/// catch cannot tell a break-window abandon apart from a host shutdown (both surface identically as
/// "the caller's <c>ct</c> fired"); only the stock worker, which owns the linked
/// <see cref="CancellationTokenSource"/> pair, can honestly distinguish the two.
/// </para>
/// </summary>
public enum LlmCallCause
{
    /// <summary>The call produced usable copy — an exact fit, or content otherwise accepted as-is
    /// (a <see cref="LlmCallOutcome.Trimmed"/> salvage still counts: the copy aired).</summary>
    Success,

    /// <summary>Our own <c>Llm:TimeoutSeconds</c> budget elapsed before a response arrived.</summary>
    Timeout,

    /// <summary>The reply came back but did not fit a length/duration constraint this project
    /// enforces — the gh-#277 family (a <c>Llm:MaxCopyChars</c> reject with no sentence-boundary
    /// salvage) and its <see cref="CrosstalkScriptWriter"/> analogues (a <c>finish_reason: length</c>
    /// truncation, a per-line budget overrun, an over-target duration estimate).</summary>
    OverLength,

    /// <summary>The copy failed the F138 truth gate (SPEC F138, STORY-350/351) — declared here so the
    /// taxonomy is complete from T330 onward, but stamped by nobody until PLAN T331 wires the gate
    /// itself at the <see cref="LlmCopyWriter"/> seam.</summary>
    TruthGateReject,

    /// <summary>A non-2xx status, a connect failure, a malformed endpoint URI, or bad JSON — any
    /// completions fault other than this call's own timeout budget elapsing (see this enum's own
    /// class remarks for the T334 doc note on where the non-2xx/no-response distinction lives
    /// instead).</summary>
    ConnectionFailure,

    /// <summary>A <see cref="CrosstalkScriptWriter"/> generation was abandoned mid-flight because a
    /// break window opened (SPEC F127.7, F140.2) — stamped only by
    /// <c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c>, the one place that can tell this apart from
    /// an ordinary caller cancellation; see this enum's own class remarks.</summary>
    CanceledByWindow,

    /// <summary>The completions endpoint returned 2xx, but nothing usable resulted: hygiene left an
    /// empty string (<see cref="LlmCopyWriter"/>), or a <see cref="CrosstalkScriptWriter"/> line
    /// matched its speaker tag correctly but was empty after cleanup (see this enum's own class
    /// remarks for why that ONE parser reject stays here while every other shape reject moved to
    /// <see cref="MalformedResponse"/>).</summary>
    EmptyCompletion,

    /// <summary>
    /// ⚠️ Added (T330 review round 1, 2026-08-20 — SPEC F139.1 amendment): the reply came back WITH
    /// content, but that content never took the shape <see cref="CrosstalkScriptParser"/> requires —
    /// an unrecognized speaker tag, a line count outside its 3-8 range (too few OR too many), a
    /// missing HOST or NEIGHBOR turn, or broken speaker alternation. See this enum's own class remarks
    /// for the full amendment rationale and the one parser reject that deliberately stays
    /// <see cref="EmptyCompletion"/> instead.
    /// </summary>
    MalformedResponse,
}
