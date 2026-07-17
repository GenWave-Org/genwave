namespace GenWave.Tts;

/// <summary>
/// Which stage of the all-or-nothing safe-segment authoring pipeline reported failure (SPEC F27.1,
/// STORY-078 AC4/AC5). Callers (P6's endpoint, P7's boot seed) map these to their own presentation —
/// e.g. P6 maps every reason to <c>502</c> per F27.3, since request-shape problems (blank text,
/// unknown library/bed ids) are validated before <see cref="SafeSegmentAuthor.AuthorAsync"/> is ever
/// called.
/// </summary>
public enum SafeSegmentFailureReason
{
    /// <summary>
    /// The TTS synthesizer threw — Kokoro unreachable, a non-2xx response, or any other synthesis
    /// fault. Nothing was written to disk (F27.1 AC4).
    /// </summary>
    SynthesisFailed,

    /// <summary>
    /// The audio mixer threw. The mixer guarantees it leaves no partial output file of its own; the
    /// raw synth clip is deleted here.
    /// </summary>
    MixFailed,

    /// <summary>
    /// The loudness analyzer threw against the final artifact. Loudness is the only measurement that
    /// gates readiness (mirrors F13.3/F17.4's cue/energy-never-gates discipline) — a loudness failure
    /// aborts the whole authoring attempt and deletes the artifact and the synth file.
    /// </summary>
    MeasurementFailed,

    /// <summary>
    /// The authored-insert write seam threw (including an unmapped Postgres foreign-key violation for
    /// an unknown library id, per its documented remarks) — the artifact and synth file are deleted
    /// and the raw exception never escapes this reason.
    /// </summary>
    InsertFailed,
}
