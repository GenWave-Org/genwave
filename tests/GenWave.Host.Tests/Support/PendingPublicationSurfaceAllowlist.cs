namespace GenWave.Host.Tests.Support;

/// <summary>
/// SPEC F218.5 / PLAN T683 (the T524 precedent): <c>QuirkRule</c> and
/// <c>PersonaCard.QuirkRules</c> (and, PLAN T684, the init-only <c>PersonaId</c>/<c>ShowId</c>/<c>ShiftKeyText</c>/
/// <c>UtteranceId</c>/<c>Text</c> members on <c>SegmentRequest</c>/<c>SegmentCopy</c>/<c>SegmentGenerated</c>; and, PLAN T685,
/// the new <c>TrackProvenAired</c> event published beside <c>TrackAired</c>; and, PLAN T693/T714, the new <c>RepeatCaught</c> event and its <c>RepeatOutcome</c>; and, PLAN T709, <c>QuirkRule.MaxPerHour</c>/<c>MinBreaksApart</c>; and, PLAN T702, <c>CrosstalkParticipant</c>/<c>FactReadOnAir</c> and the init-only <c>Host</c>/<c>Neighbor</c>/<c>FactRead</c> members; and, PLAN T702a, <c>SegmentRequest.ContextProviderKey</c>; and, PLAN T751, <c>PersonaCard.Voices</c>/<c>VoicesByEngine</c>/<c>WithVoice</c>; and, PLAN T726, <c>HandoffParticipants</c> and the init-only <c>Handoff</c> members on <c>SegmentRequest</c>/<c>MediaItem</c>/<c>TrackAired</c>) are an additive contract change that landed in
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
        "GenWave.Core.Domain.QuirkRule::CTOR (System.String Text, System.Nullable<System.Int32> MaxPerShift = null, System.Nullable<System.Double> PerBreakChance = null, System.Nullable<System.Int32> MaxPerHour = null, System.Nullable<System.Int32> MinBreaksApart = null)",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.Nullable<System.Double> PerBreakChance { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.Nullable<System.Int32> MaxPerHour { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.Nullable<System.Int32> MaxPerShift { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.Nullable<System.Int32> MinBreaksApart { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::PROPERTY System.String Text { public get; public init; }",
        "GenWave.Core.Domain.QuirkRule::TYPE class : none [System.IEquatable<GenWave.Core.Domain.QuirkRule>]",
        "GenWave.Core.Domain.SegmentCopy::PROPERTY System.Nullable<System.Int64> UtteranceId { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.Nullable<System.Int64> PersonaId { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.Nullable<System.Int64> ShowId { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.String ShiftKeyText { public get; public init; }",
        // PLAN T702a: the context provider's key on a ContextSegment request.
        "GenWave.Core.Domain.SegmentRequest::PROPERTY System.String ContextProviderKey { public get; public init; }",
        // PLAN T693: RepeatCaught, a new additive event (the repeat check's booth-log signal).
        "GenWave.Core.Events.RepeatCaught::CTOR (System.Int64 PersonaId, System.String Phrase, System.Nullable<GenWave.Core.Events.RepeatOutcome> Outcome = null)",
        "GenWave.Core.Events.RepeatCaught::PROPERTY System.Nullable<GenWave.Core.Events.RepeatOutcome> Outcome { public get; public init; }",
        "GenWave.Core.Events.RepeatCaught::PROPERTY System.Int64 PersonaId { public get; public init; }",
        "GenWave.Core.Events.RepeatCaught::PROPERTY System.String Phrase { public get; public init; }",
        "GenWave.Core.Events.RepeatCaught::TYPE class : GenWave.Core.Events.StationEvent [System.IEquatable<GenWave.Core.Events.RepeatCaught>,System.IEquatable<GenWave.Core.Events.StationEvent>]",
        // PLAN T714: RepeatOutcome, the F218.38 outcome the repeat-caught row ends with.
        "GenWave.Core.Events.RepeatOutcome::FIELD System.Int32 value__",
        "GenWave.Core.Events.RepeatOutcome::FIELD static GenWave.Core.Events.RepeatOutcome Dropped const = 3",
        "GenWave.Core.Events.RepeatOutcome::FIELD static GenWave.Core.Events.RepeatOutcome ReAsked const = 1",
        "GenWave.Core.Events.RepeatOutcome::FIELD static GenWave.Core.Events.RepeatOutcome Template const = 2",
        "GenWave.Core.Events.RepeatOutcome::FIELD static GenWave.Core.Events.RepeatOutcome Trimmed const = 0",
        "GenWave.Core.Events.RepeatOutcome::TYPE enum : none [System.IComparable,System.IConvertible,System.IFormattable,System.ISpanFormattable]",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.Nullable<System.Int64> PersonaId { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.Nullable<System.Int64> ShowId { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.Nullable<System.Int64> UtteranceId { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY System.String Text { public get; public init; }",
        // PLAN T685: TrackProvenAired, a new additive event beside TrackAired (the feeder's "proven aired" signal).
        "GenWave.Core.Events.TrackProvenAired::CTOR (System.String MediaId, System.DateTimeOffset ProvenAt)",
        "GenWave.Core.Events.TrackProvenAired::PROPERTY System.DateTimeOffset ProvenAt { public get; public init; }",
        "GenWave.Core.Events.TrackProvenAired::PROPERTY System.String MediaId { public get; public init; }",
        "GenWave.Core.Events.TrackProvenAired::TYPE class : GenWave.Core.Events.StationEvent [System.IEquatable<GenWave.Core.Events.StationEvent>,System.IEquatable<GenWave.Core.Events.TrackProvenAired>]",
        // PLAN T702: the crosstalk's two participants and the fact read on air, additive init-only members the episode accrual reads.
        "GenWave.Core.Domain.CrosstalkAiredScript::PROPERTY GenWave.Core.Domain.CrosstalkParticipant Host { public get; public init; }",
        "GenWave.Core.Domain.CrosstalkAiredScript::PROPERTY GenWave.Core.Domain.CrosstalkParticipant Neighbor { public get; public init; }",
        "GenWave.Core.Domain.CrosstalkParticipant::CTOR (System.Int64 PersonaId, System.String Name)",
        "GenWave.Core.Domain.CrosstalkParticipant::PROPERTY System.Int64 PersonaId { public get; public init; }",
        "GenWave.Core.Domain.CrosstalkParticipant::PROPERTY System.String Name { public get; public init; }",
        "GenWave.Core.Domain.CrosstalkParticipant::TYPE class : none [System.IEquatable<GenWave.Core.Domain.CrosstalkParticipant>]",
        "GenWave.Core.Domain.FactReadOnAir::CTOR (System.String Key, System.String Text)",
        "GenWave.Core.Domain.FactReadOnAir::PROPERTY System.String Key { public get; public init; }",
        "GenWave.Core.Domain.FactReadOnAir::PROPERTY System.String Text { public get; public init; }",
        "GenWave.Core.Domain.FactReadOnAir::TYPE class : none [System.IEquatable<GenWave.Core.Domain.FactReadOnAir>]",
        "GenWave.Core.Domain.SegmentCopy::PROPERTY GenWave.Core.Domain.FactReadOnAir FactRead { public get; public init; }",
        "GenWave.Core.Events.SegmentGenerated::PROPERTY GenWave.Core.Domain.FactReadOnAir FactRead { public get; public init; }",
        // PLAN T726: a handoff's two personas, carried SegmentRequest -> MediaItem -> TrackAired for the episode accrual.
        "GenWave.Core.Domain.HandoffParticipants::CTOR (GenWave.Core.Domain.CrosstalkParticipant Outgoing, GenWave.Core.Domain.CrosstalkParticipant Incoming, System.DateTimeOffset BoundaryAt)",
        "GenWave.Core.Domain.HandoffParticipants::PROPERTY System.DateTimeOffset BoundaryAt { public get; public init; }",
        "GenWave.Core.Domain.HandoffParticipants::PROPERTY GenWave.Core.Domain.CrosstalkParticipant Incoming { public get; public init; }",
        "GenWave.Core.Domain.HandoffParticipants::PROPERTY GenWave.Core.Domain.CrosstalkParticipant Outgoing { public get; public init; }",
        "GenWave.Core.Domain.HandoffParticipants::TYPE class : none [System.IEquatable<GenWave.Core.Domain.HandoffParticipants>]",
        "GenWave.Core.Domain.MediaItem::PROPERTY GenWave.Core.Domain.HandoffParticipants Handoff { public get; public init; }",
        "GenWave.Core.Domain.SegmentRequest::PROPERTY GenWave.Core.Domain.HandoffParticipants Handoff { public get; public init; }",
        "GenWave.Core.Events.TrackAired::PROPERTY GenWave.Core.Domain.HandoffParticipants Handoff { public get; public init; }",
        // PLAN T751: the per-engine voice map and its one lockstep rule (F219.7).
        "GenWave.Core.Domain.PersonaCard::PROPERTY System.Collections.Generic.IReadOnlyDictionary<System.String,System.String> Voices { public get; public init; }",
        "GenWave.Core.Domain.PersonaCard::METHOD System.Collections.Generic.IReadOnlyDictionary<System.String,System.String> VoicesByEngine()",
        "GenWave.Core.Domain.PersonaCard::METHOD GenWave.Core.Domain.PersonaCard WithVoice(GenWave.Core.Domain.VoiceSpec voice)",
        // PLAN T753: a speaker with no voice for the active engine (F219.9), and the flag that carries it to the render.
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.Boolean VoiceMissing { public get; public init; }",
        "GenWave.Core.Domain.SpeakerSnapshot::PROPERTY System.Boolean VoiceOverridesRow { public get; public init; }",
        "GenWave.Core.Domain.StationIdentity::PROPERTY System.Boolean VoiceMissing { public get; public init; }",
        "GenWave.Core.Events.VoiceMissing::CTOR (System.String Speaker, System.Nullable<System.Int64> PersonaId = null)",
        "GenWave.Core.Events.VoiceMissing::PROPERTY System.Nullable<System.Int64> PersonaId { public get; public init; }",
        "GenWave.Core.Events.VoiceMissing::PROPERTY System.String Speaker { public get; public init; }",
        "GenWave.Core.Events.VoiceMissing::TYPE class : GenWave.Core.Events.StationEvent [System.IEquatable<GenWave.Core.Events.StationEvent>,System.IEquatable<GenWave.Core.Events.VoiceMissing>]",
        // PLAN T767: a talk dropped for a render past the engine's time limit (F219.26).
        "GenWave.Core.Events.TalkDroppedLate::CTOR (System.String Kind, System.String PersonaName = null)",
        "GenWave.Core.Events.TalkDroppedLate::PROPERTY System.String Kind { public get; public init; }",
        "GenWave.Core.Events.TalkDroppedLate::PROPERTY System.String PersonaName { public get; public init; }",
        "GenWave.Core.Events.TalkDroppedLate::TYPE class : GenWave.Core.Events.StationEvent [System.IEquatable<GenWave.Core.Events.StationEvent>,System.IEquatable<GenWave.Core.Events.TalkDroppedLate>]",
    ];
}
