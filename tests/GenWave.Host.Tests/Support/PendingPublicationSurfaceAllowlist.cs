namespace GenWave.Host.Tests.Support;

/// <summary>
/// PLAN T524 round-2 F1 ruling: <c>SpeakerSnapshot</c>/<c>SpeechCorrection</c> and
/// <see cref="GenWave.Core.Domain.SegmentRequest.Speaker"/> (SPEC F189.1 — an additive contract
/// change, so the NuGet gets a minor bump, gh-#772) landed in <c>src/GenWave.Abstractions</c>
/// BEFORE PLAN T530 (which regenerates <c>Fixtures/abstractions-5.7.0-surface.txt</c> from the
/// newly published package and renames it) ships. Until then, <c>Story431</c>'s
/// <c>ThePackageSurfaceDiffVs570IsEmpty</c> fact tolerates EXACTLY these 16 added lines — named
/// line-by-line, not a type/namespace wildcard — as additions only; any other addition, or any
/// removal, still fails the fact. PLAN T530 deletes this file in the same PR that regenerates the
/// baseline.
/// </summary>
internal static class PendingPublicationSurfaceAllowlist
{
    public static readonly IReadOnlyList<string> Lines =
    [
        "GenWave.Core.Domain.SegmentRequest::PROPERTY GenWave.Core.Domain.SpeakerSnapshot Speaker { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::CTOR (System.Nullable<System.Int64> PersonaId, System.String PersonaName, System.String Voice, System.Double Pace, System.Collections.Generic.IReadOnlyList<GenWave.Core.Domain.PronunciationRule> Rules, System.Collections.Generic.IReadOnlyList<GenWave.Core.Domain.SpeechCorrection> Corrections, System.String ContentHash)",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.Collections.Generic.IReadOnlyList<GenWave.Core.Domain.PronunciationRule> Rules { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.Collections.Generic.IReadOnlyList<GenWave.Core.Domain.SpeechCorrection> Corrections { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.Double Pace { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.Nullable<System.Int64> PersonaId { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.String ContentHash { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.String PersonaName { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.String Voice { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::TYPE class : none [System.IEquatable<GenWave.Core.Domain.SpeakerSnapshot>]",
        "GenWave.Core.Domain.SpeechCorrection::CTOR (System.String From, System.String To)",
        "GenWave.Core.Domain.SpeechCorrection::PROPERTY System.String From { public get; public init; }",
        "GenWave.Core.Domain.SpeechCorrection::PROPERTY System.String To { public get; public init; }",
        "GenWave.Core.Domain.SpeechCorrection::PROPERTY System.String WhenFollowedBy { public get; public init; }",
        "GenWave.Core.Domain.SpeechCorrection::PROPERTY System.String WhenPrecededBy { public get; public init; }",
        "GenWave.Core.Domain.SpeechCorrection::TYPE class : none [System.IEquatable<GenWave.Core.Domain.SpeechCorrection>]",
    ];
}
