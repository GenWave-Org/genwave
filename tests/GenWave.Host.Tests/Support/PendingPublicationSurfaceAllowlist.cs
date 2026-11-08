namespace GenWave.Host.Tests.Support;

/// <summary>
/// SPEC F218.5 / PLAN T683 (the T524 precedent): <c>QuirkRule</c> and
/// <c>PersonaCard.QuirkRules</c> (and, PLAN T684, the init-only <c>PersonaId</c>/<c>ShowId</c>/<c>ShiftKeyText</c>/
/// <c>UtteranceId</c>/<c>Text</c> members on <c>SegmentRequest</c>/<c>SegmentCopy</c>/<c>SegmentGenerated</c>) are an additive contract change that landed in
/// <c>src/GenWave.Abstractions</c> BEFORE the package that carries it is published. The committed
/// baseline fixture stays byte-identical to the published surface (never hand-edited); the gap is
/// tolerated only by passing these EXACT added lines — named line by line, no wildcard — to
/// <see cref="PublicSurface.DiffAgainst"/> in Story431's and Story483's surface facts. Any other
/// addition, or any removal, still fails. The next release cut regenerates the fixture from the
/// published package and deletes this file in the same PR.
/// </summary>
public static class PendingPublicationSurfaceAllowlist
{
    public static readonly IReadOnlyList<string> Lines =
    [
        "GenWave.Core.Domain.PersonaCard::PROPERTY System.Collections.Generic.IReadOnlyList<GenWave.Core.Domain.QuirkRule> QuirkRules { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::CTOR (System.String Text, System.Nullable<System.Int32> MaxPerShift = null, System.Nullable<System.Double> PerBreakChance = null)",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.Nullable<System.Double> PerBreakChance { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.Nullable<System.Int32> MaxPerShift { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.String Text { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::TYPE class : none [System.IEquatable<GenWave.Core.Domain.QuirkRule>]",
        "GenWave.Core.Domain.SegmentCopy::PROPERTY System.Nullable<System.Int64> UtteranceId { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.Nullable<System.Int64> PersonaId { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.Nullable<System.Int64> ShowId { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.String ShiftKeyText { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.Nullable<System.Int64> PersonaId { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.Nullable<System.Int64> ShowId { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.Nullable<System.Int64> UtteranceId { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.String Text { public get; public init; }",
    ];
}
