namespace GenWave.VoiceOff.Serving;

/// <summary>The rules a judgement must meet before it is written (SPEC F217.7).</summary>
internal static class JudgementCheck
{
    internal const int MaxNotesLength = 2000;
    internal const int MinStars = 1;
    internal const int MaxStars = 5;

    /// <returns>A plain sentence naming the first broken rule, or null when the request is acceptable.</returns>
    internal static string? Problem(JudgementRequest request, ServeRun run)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(run);

        HashSet<string> seen = new(StringComparer.Ordinal);
        // StrictJson refuses a null member but does not look inside lists and maps, so a null element is checked here.
        foreach (string? id in request.Order)
        {
            if (id is null)
            {
                return "order has an empty entry.";
            }

            if (!run.HasCard(id))
            {
                return $"order names a card that is not in this run: {Show(id)}.";
            }

            if (!seen.Add(id))
            {
                return $"order names card {id} more than once.";
            }
        }

        foreach ((string id, CardJudgement? card) in request.Cards)
        {
            if (card is null)
            {
                return $"cards has an empty entry for {Show(id)}.";
            }

            string? problem = run.HasCard(id) ? CardProblem(id, card) : $"cards names a card that is not in this run: {Show(id)}.";
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static string? CardProblem(string id, CardJudgement card)
    {
        if (card.Winces < 0)
        {
            return $"Card {id}: winces must be 0 or more.";
        }

        if (card.Stars is < MinStars or > MaxStars)
        {
            return $"Card {id}: stars must be {MinStars} to {MaxStars}, or null while unrated.";
        }

        return card.Notes.Length > MaxNotesLength ? $"Card {id}: notes are longer than {MaxNotesLength} characters." : null;
    }

    // An id that is not in the run may be anything the client sent; keep the echo short and on one line.
    private static string Show(string id) =>
        "\"" + new string([.. id.Take(40).Select(c => char.IsControl(c) ? '?' : c)]) + "\"";
}
