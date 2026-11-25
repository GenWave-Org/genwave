namespace GenWave.Tts;

/// <summary>
/// The rules-mode flow <see cref="AdScriptWriter"/> hands one ad to (SPEC F220.10-F220.16, F220.29, PLAN T802). An interface because
/// the ad snapshot and the shape checks live in GenWave.Ads, which this project must never reference (L10); GenWave.Ads supplies
/// the implementation, as it does <see cref="IAdShadowCheck"/>.
/// </summary>
public interface IAdRulesFlow
{
    /// <summary>True when the live <c>TalkRules:Mode</c> is rules. The writer asks once per ad. Never throws.</summary>
    bool IsOn();

    /// <summary>
    /// Asks, runs the fixed clean-up, judges, and re-asks once when a switched-on rule flags the ad (F220.13). The writer owns the
    /// model call and the hygiene (through <paramref name="calls"/>) and the ring entry of the final attempt. Faults from the
    /// calls are returned, not thrown; only the caller's cancellation escapes.
    /// </summary>
    Task<AdRulesResult> RunAsync(AdScriptWriteRequest request, string userPrompt, AdRulesCalls calls, CancellationToken ct);

    /// <summary>
    /// Tells the recording rules about an accepted ad and writes the accepted attempt's row (F220.15, F220.29). The writer calls
    /// it after it has recorded the accepted call. Never throws except for the caller's own cancellation.
    /// </summary>
    ValueTask AcceptedAsync(AdRulesAccepted accepted, CancellationToken ct);
}
