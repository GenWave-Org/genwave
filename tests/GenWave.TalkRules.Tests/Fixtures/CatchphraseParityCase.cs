namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>
/// One DJ-line reply for the catchphrase-limits parity. Nothing else is set, so only the quirk and tagline checks can trip:
/// the speaker's earlier lines share no run of five words with the reply.
/// </summary>
/// <param name="Copy">The cleaned reply.</param>
/// <param name="Recent">The speaker's earlier lines, newest first; they decide which taglines are used up.</param>
/// <param name="Tagline">The card's tagline field, when it has one.</param>
/// <param name="CappedQuirk">A quirk the card limits to once a shift, when there is one.</param>
/// <param name="CappedQuirkOffered">True when the selector handed that quirk to the writer this break.</param>
internal sealed record CatchphraseParityCase(
    string Copy,
    IReadOnlyList<RecentSaid> Recent,
    string Tagline = "",
    string? CappedQuirk = null,
    bool CappedQuirkOffered = false);
