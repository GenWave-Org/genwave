namespace GenWave.Host.Pronunciations;

/// <summary>
/// Derives candidate IPA for an operator-authored respelling (SPEC F126.2, STORY-324, PLAN T278) —
/// the pronunciation rules editor's "Derive" assist. Purely an authoring aid: the result is handed
/// back to <c>PronunciationDerivationController</c>'s caller raw, for the operator to audition
/// (STORY-323's <c>POST /api/tts/preview</c>) and adjust before ever saving it as a
/// <c>PronunciationsController</c> row — this abstraction has no opinion about persistence, and
/// nothing on the render/playout path ever resolves it (see
/// <c>GenWave.Host.Tests.Specs.FeatureRespellOracle.ScenarioTheOracleNeverSitsOnARenderPath</c>).
///
/// A seam over the vendored espeak-ng binary (<see cref="EspeakRespellOracle"/>) so the endpoint
/// itself is unit-testable without a real process on the test host — one real-binary integration
/// fact covers the actual CLI contract separately (detect-and-skip when the test host itself lacks
/// espeak-ng, the docker-gated MediaLibrary precedent).
/// </summary>
public interface IRespellOracle
{
    /// <summary>
    /// <see langword="true"/> until this process instance has confirmed espeak-ng is NOT
    /// launchable — starts optimistic and latches permanently false the first time starting the
    /// process fails for a reason that means the BINARY ITSELF is unusable (missing, unreadable, or
    /// not executable), never for a transient failure to fork this one attempt (T278: "once absent,
    /// absent until restart" — an image either ships the apt package or it doesn't; there is no
    /// scenario where re-probing per request would ever recover a different answer for THAT
    /// question, though a resource-exhaustion hiccup says nothing about the next attempt and does
    /// not flip this). A caller checks this BEFORE calling <see cref="DeriveAsync"/> to answer the
    /// endpoint's 501 case without paying for a process launch attempt on every request once the
    /// outcome is already known.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Runs the oracle over <paramref name="respelling"/> and returns the candidate IPA it printed —
    /// trimmed AND with every internal whitespace run (including a clause-boundary newline) collapsed
    /// to a single space, never raw multi-line text. Returns <see langword="null"/> for any failure —
    /// a non-zero exit, empty/unparseable output, the render exceeding its own timeout budget, or the
    /// binary turning out to be permanently unusable right here (this can still happen even when a
    /// caller already checked <see cref="IsAvailable"/> first — that check is a fast pre-filter, not
    /// a guarantee against a subsequent race — but a merely transient failure to fork THIS attempt
    /// answers <see langword="null"/> too, without flipping <see cref="IsAvailable"/>, so the caller
    /// re-checks it to tell the two apart). <paramref name="respelling"/> is passed through to the
    /// underlying process as ONE argv entry — never shell-interpreted, and never interpreted as an
    /// OPTION either even when it starts with <c>-</c> (the implementation's own POSIX end-of-options
    /// marker makes a leading-dash respelling inert, not merely un-shell-expanded) — so it is safe to
    /// call with arbitrary (already length-capped) operator input.
    /// </summary>
    Task<string?> DeriveAsync(string respelling, CancellationToken ct);
}
