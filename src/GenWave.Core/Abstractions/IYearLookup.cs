namespace GenWave.Core.Abstractions;

/// <summary>
/// Looks up a track's original release year from an external metadata source when its embedded tags
/// carry none (SPEC F48, closes gitea-#208) — the one deliberate, disable-able exception to the
/// "embedded-tag metadata only" scope rule, carved out for year alone. This is the offline enrichment
/// path — it must never run on the real-time playout loop.
///
/// Returns <see langword="null"/> whenever no confident match is found: no candidate, a below-threshold
/// match score, an artist mismatch, or any lookup failure (network, malformed response, timeout). This
/// is a legal, expected outcome, never signalled by an exception past this boundary (F48.1–F48.2).
/// </summary>
public interface IYearLookup
{
    Task<int?> TryLookupAsync(string artist, string title, string? album, CancellationToken ct);
}
