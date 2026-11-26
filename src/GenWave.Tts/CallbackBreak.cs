using GenWave.Core.Domain;

namespace GenWave.Tts;

/// <summary>
/// What the callback planner needs to know about one break (SPEC F218.45): who speaks, the kind, and the station and track
/// names a callback must not lean on. Built from the writer's <see cref="SegmentRequest"/> or from a rule's snapshot, so both
/// ask the planner the same question.
/// </summary>
public sealed record CallbackBreak(
    SegmentKind Kind, long PersonaId, string StationName, string? Title, string? Artist, string? Album)
{
    /// <summary>The break a segment request describes, spoken by <paramref name="personaId"/>.</summary>
    public static CallbackBreak Of(SegmentRequest request, long personaId) =>
        new(request.Kind, personaId, request.StationName, request.Track?.Title, request.Track?.Artist, request.Track?.Album);
}
