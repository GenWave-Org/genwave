using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SPEC F107.1 — the context seam: a source of on-air facts (weather, this-day-in-history, and any
/// future kind — including a commercial edition's ad content, the demonstrated third-party need this
/// contract was cut for, F105.6). A provider returns <b>facts only — never prose-for-air, never
/// audio</b>; turning facts into spoken copy is the TTS/copywriter pipeline's job, and choosing a
/// voice is orchestration policy — neither is this seam's concern. Deliberately minimal: demoting
/// anything off this MIT surface is a breaking change, so it carries no request object and no
/// configuration parameter — a provider is constructed already knowing everything it needs to fetch.
/// </summary>
public interface IContextProvider
{
    /// <summary>
    /// The provider's stable identity — constant across restarts and deploys. Doubles as the
    /// <c>SpeechDeferralQueue</c> per-<c>(kind, discriminator)</c> supersede key (F107.4, so a due
    /// weather fact never silently supersedes a due history fact) and the settings segment prefix
    /// every provider's live configuration reads from (<c>Context:{Key}:*</c> — e.g.
    /// <c>Context:Weather:Enabled</c>, <c>Context:Weather:SegmentCadenceMinutes</c>).
    ///
    /// <b>Unique and shaped like a path segment</b> (T221 review carry-forward): must be unique
    /// across every provider registered with the pipeline, and restricted to lowercase ASCII
    /// letters, digits, and hyphens — it is both a settings path segment and a queue discriminator,
    /// neither of which tolerates whitespace, casing collisions, or punctuation. The pipeline
    /// (<c>GenWave.Context.ContextPipeline</c>) fails fast — an <see cref="ArgumentException"/> at
    /// construction — on a duplicate or invalid key; it is never a runtime-discovered fault.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Fetches this provider's current facts, or null when there is nothing to say right now
    /// (disabled, stale, an upstream failure, or simply no news). <b>Null is never an error</b> — the
    /// pipeline treats it as ordinary skip-never-silence input (F107.6): no segment, no patter line,
    /// music continues unaffected.
    ///
    /// <b>Throwing is equally ordinary</b> (T221 review carry-forward): an implementation MAY throw
    /// instead of returning null — the pipeline catches it and applies the exact same skip-never-silence
    /// handling (F107.6) as a null return, one Information line per cadence slot naming the cause. A
    /// provider therefore never needs its own try/catch purely to turn a failure into null; throwing
    /// straight through an upstream fault is a valid, unwrapped implementation.
    /// </summary>
    Task<ContextContent?> FetchAsync(CancellationToken ct);
}
