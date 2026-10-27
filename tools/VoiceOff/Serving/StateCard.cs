namespace GenWave.VoiceOff.Serving;

/// <summary>
/// One card of the run in <c>/state</c>. <paramref name="Details"/> is null when the run lists the card but its file is gone from the cards folder; the card
/// is still shown (and still playable if it rendered) so a judgement already made about it is not lost.
/// </summary>
internal sealed record StateCard(string Id, string Host, bool Rendered, string? Reason, StateCardDetails? Details);
