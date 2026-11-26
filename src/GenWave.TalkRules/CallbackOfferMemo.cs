using GenWave.Core.Domain;

namespace GenWave.TalkRules;

/// <summary>What the <c>callbacks</c> rule offered this break, left on the snapshot under <see cref="RuleId"/> so it can settle the offer once the talk is accepted (F220.8, F220.15). Nobody else reads it.</summary>
/// <param name="Ask">The ask the planner wrote an offer row for: the offer id, the one memory, and the texts the word rule leaves out when it settles.</param>
public sealed record CallbackOfferMemo(CallbackAsk Ask)
{
    /// <summary>The rule id the memo is keyed by.</summary>
    public const string RuleId = "callbacks";
}
