namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>judgement.json</c> (SPEC F217.8): the whole document, last write wins, one judge. <paramref name="Order"/> runs best to worst and may name
/// fewer cards than the run has; <paramref name="Cards"/> holds the cards the judge has touched.
/// </summary>
internal sealed record Judgement(DateTimeOffset SavedAt, IReadOnlyList<string> Order, IReadOnlyDictionary<string, CardJudgement> Cards);
