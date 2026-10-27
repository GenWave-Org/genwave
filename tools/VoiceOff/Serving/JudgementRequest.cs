namespace GenWave.VoiceOff.Serving;

/// <summary>
/// The body of <c>PUT /judgement</c>: a <see cref="Judgement"/> as the page holds it. <paramref name="SavedAt"/> is accepted so the page may send the
/// whole document, and ignored: the server's clock decides when it was saved.
/// </summary>
internal sealed record JudgementRequest(
    IReadOnlyList<string> Order,
    IReadOnlyDictionary<string, CardJudgement> Cards,
    DateTimeOffset? SavedAt = null);
