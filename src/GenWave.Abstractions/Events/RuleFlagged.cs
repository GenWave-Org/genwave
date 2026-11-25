namespace GenWave.Core.Events;

/// <summary>
/// A switched-on talk rule flagged a DJ's line or chat and the flag changed what happened next (SPEC F220.16,
/// STORY-570, PLAN T800, T801). <paramref name="RuleName"/> is the rule's plain name, <paramref name="Speaker"/> the
/// DJ's name (a chat names its host). Published once per flagging rule: <see cref="RuleFlagOutcome.ReAsked"/> after the
/// first attempt, <see cref="RuleFlagOutcome.Template"/> or <see cref="RuleFlagOutcome.Dropped"/> when the re-ask was
/// flagged again.
/// </summary>
public sealed record RuleFlagged(long PersonaId, string RuleName, string Speaker, RuleFlagOutcome Outcome) : StationEvent
{
    /// <summary>Whether the flagged talk was a line or a chat; a line unless a chat says otherwise.</summary>
    public RuleFlagTalk Talk { get; init; } = RuleFlagTalk.Line;
}
