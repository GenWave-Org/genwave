namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// The LLM call inspector's in-memory ring (SPEC F73.1-F73.4, STORY-196, T41) — a DEBUG LENS, NOT
/// AN AUDIT TRAIL. Keeps the last <see cref="LlmOptions.CallRingCapacity"/> (default ~50) completed
/// LLM calls, newest first: on-air renders, Soft-cadence attempts, and operator previews alike —
/// every call that reaches <see cref="LlmCopyWriter.RequestCompletionAsync"/>, whichever of
/// <see cref="LlmCopyWriter.WriteAsync"/>/<see cref="LlmCopyWriter.WritePreviewAsync"/> it arrived
/// through. Registered as a SINGLETON with NO persistence dependency of any kind (F73.3, T41 AC3):
/// this class's only constructor dependency is <see cref="IOptionsMonitor{LlmOptions}"/> for its own
/// live capacity — nothing here ever touches disk or a database, so a process restart clears it by
/// construction, not by some explicit "clear" step a future change could forget to call.
///
/// <para>
/// REVISIT TRIGGER (SPEC F73.4, recorded here and in <c>docs/SPEC.md</c>): if org-grade spend/audit
/// requirements ever apply to LLM usage, that becomes a SEPARATE persistent feature — its own table,
/// its own retention policy, mirroring the booth log's own insert-time-retention shape (SPEC F72.3)
/// — never an extension of this ring. Prompts embed persona/operator content nobody has consented to
/// seeing written to disk; this ring's entire value proposition is that it is a live, ephemeral
/// window an admin can glance at, not a record anything is ever expected to keep.
/// </para>
///
/// Thread-safe: a single <c>lock</c> guards the ring (mirrors
/// <c>GenWave.Host.Playout.PlayHistoryService</c>'s own shape, simplified to one global ring rather
/// than one per station — the ring inspector has no per-station concept, SPEC F73.1).
/// </summary>
public sealed class LlmCallRing(IOptionsMonitor<LlmOptions> options)
{
    readonly object gate = new();
    readonly LinkedList<LlmCallRecord> ring = new();
    long nextSeq;

    /// <summary>
    /// Records one completed call, success or failure — the single recording point every caller
    /// (<see cref="LlmCopyWriter.RequestCompletionAsync"/>, on behalf of both
    /// <see cref="LlmCopyWriter.WriteAsync"/> and <see cref="LlmCopyWriter.WritePreviewAsync"/>)
    /// funnels through, so on-air renders, Soft-cadence attempts, and previews are all captured by
    /// construction rather than needing three separate call sites to stay in sync. Capacity is read
    /// fresh from <see cref="IOptionsMonitor{LlmOptions}.CurrentValue"/> on every call (mirrors
    /// <c>PlayHistoryService.Push</c>'s own live-capacity seam), so a live edit to
    /// <c>Llm:CallRingCapacity</c> trims (or grows) the ring on the very next record.
    /// </summary>
    public void Record(
        string? promptSystem, string? promptUser, string? response, DateTimeOffset startedAt,
        long elapsedMs, LlmCallOutcome outcome, string? statusDetail, DegradationMode mode)
    {
        lock (gate)
        {
            var record = new LlmCallRecord(
                ++nextSeq, promptSystem, promptUser, response, startedAt, elapsedMs, outcome, statusDetail, mode);
            ring.AddFirst(record);   // newest first

            var capacity = options.CurrentValue.CallRingCapacity;
            while (ring.Count > capacity)
                ring.RemoveLast();
        }
    }

    /// <summary>Every record currently held, newest first (SPEC F73.1-F73.2) — the admin endpoint's read seam.</summary>
    public IReadOnlyList<LlmCallRecord> Snapshot()
    {
        lock (gate)
        {
            return ring.ToList();
        }
    }
}
