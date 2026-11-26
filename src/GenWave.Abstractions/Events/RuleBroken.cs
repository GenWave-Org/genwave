namespace GenWave.Core.Events;

/// <summary>
/// A talk rule's shaper or recorder threw (SPEC F220.9, STORY-570, PLAN T804a). It never changes the outcome, so the booth row has
/// no outcome: "&lt;Rule name&gt; could not prepare|save &lt;DJ&gt;'s line|chat|ad". A separate event from <see cref="RuleFlagged"/>
/// because a flag asks for a re-ask and a breakage never does. <paramref name="Speaker"/> is the DJ's name (a chat names its host) or,
/// for an ad, the sponsor, with <paramref name="PersonaId"/> null.
/// </summary>
public sealed record RuleBroken(long? PersonaId, string RuleName, string Speaker, RuleBrokenStep Step) : StationEvent
{
    /// <summary>Whether the talk was a line, a chat or an ad; a line unless the publisher says otherwise.</summary>
    public RuleFlagTalk Talk { get; init; } = RuleFlagTalk.Line;
}
