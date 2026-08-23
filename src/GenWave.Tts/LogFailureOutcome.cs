namespace GenWave.Tts;

/// <summary>
/// What <see cref="LlmCopyWriter"/>'s one shared failure-WARN helper says happens next (SPEC F34.4,
/// F35.6, F144.4, PLAN T342) — the outcome clause every <c>LogFailure</c> call reports, so the SAME
/// wording never drifts across the three seams that share that one helper.
/// </summary>
internal enum LogFailureOutcome
{
    /// <summary>The ordinary on-air/Soft-cadence miss (SPEC F34.4, F69.7): degrades to
    /// <c>TemplateCopyWriter</c>'s own copy.</summary>
    FallingBackToTemplate,

    /// <summary>An admin persona preview miss (SPEC F35.6): reported to the caller as a failure,
    /// never silently substituted.</summary>
    ReportingToPreviewCaller,

    /// <summary>An owner announcement's flavored-path miss (SPEC F144.4, PLAN T342): degrades to the
    /// F144.2 verbatim read of the owner's own message — never a template.</summary>
    FallingBackToVerbatimRead,
}
