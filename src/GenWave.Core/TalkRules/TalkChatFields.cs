namespace GenWave.Core.TalkRules;

/// <summary>
/// Chat-only snapshot fields: the length limits the crosstalk parser judged the exchange against, and which persona
/// stood on each side. <see cref="TalkSnapshot.Speakers"/> lists only sides that have a persona id, so these ids are how a
/// rule maps a line's role label ("HOST" / "NEIGHBOR") to a speaker.
/// </summary>
/// <param name="DurationTargetSeconds">The live <c>Crosstalk:DurationTargetSeconds</c> the exchange was judged against.</param>
/// <param name="MaxLineChars">The per-line character budget (<c>Llm:MaxCopyChars</c>) the parser enforced.</param>
/// <param name="CharsPerSecond">The spoken-rate constant behind the duration estimate (total characters / this).</param>
/// <param name="HostPersonaId">The persona on the HOST side; null when the caller did not name one.</param>
/// <param name="NeighborPersonaId">The persona on the NEIGHBOR side; null when the caller did not name one.</param>
/// <param name="LineChars">The lengths the parser measured, sound tags kept, one per entry of <see cref="TalkSnapshot.Lines"/> (which are tag-stripped). Their sum over <paramref name="CharsPerSecond"/> is the duration estimate; each is compared to <paramref name="MaxLineChars"/>.</param>
public sealed record TalkChatFields(
    int DurationTargetSeconds,
    int MaxLineChars,
    double CharsPerSecond,
    long? HostPersonaId,
    long? NeighborPersonaId,
    TalkList<int> LineChars);
