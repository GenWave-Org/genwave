namespace GenWave.VoiceOff.Serving;

/// <summary>
/// <c>GET /state</c>. <paramref name="Cards"/> are in display order, best first: the saved order, then every card it does not name in id order.
/// <paramref name="Judgement"/> is the saved document as written, null before the first save.
/// </summary>
internal sealed record StateReply(string RunId, IReadOnlyList<StateCard> Cards, Judgement? Judgement)
{
    internal static StateReply Build(ServeRun run, Judgement? judgement)
    {
        ArgumentNullException.ThrowIfNull(run);
        string[] named = [.. (judgement?.Order ?? []).Where(run.HasCard).Distinct(StringComparer.Ordinal)];
        IEnumerable<string> ordered = named.Concat(run.CardIds.Except(named, StringComparer.Ordinal));
        return new StateReply(run.RunId, [.. ordered.Select(run.Describe)], judgement);
    }
}
