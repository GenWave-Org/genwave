using System.Text.Json.Serialization;

namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>judgement.json</c> (SPEC F217.8): the whole document, last write wins, one judge. <paramref name="Order"/> runs best to worst and may name
/// fewer cards than the run has; <paramref name="Cards"/> holds the cards the judge has touched. <paramref name="Remarks"/> is free text the judge
/// adds to the file by hand: <c>score</c> prints it under the ranking, the page neither edits nor sends it, and <c>serve</c> keeps it across saves.
/// </summary>
internal sealed record Judgement(
    DateTimeOffset SavedAt,
    IReadOnlyList<string> Order,
    IReadOnlyDictionary<string, CardJudgement> Cards,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Remarks = null);
