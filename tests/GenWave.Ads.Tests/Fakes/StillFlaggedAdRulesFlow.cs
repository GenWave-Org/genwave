namespace GenWave.Ads.Tests.Fakes;

using GenWave.Tts;

/// <summary>A rules-mode flow whose every ad is still flagged after the re-ask, with the given reason (PLAN T802).</summary>
public sealed class StillFlaggedAdRulesFlow(string reason) : IAdRulesFlow
{
    public bool IsOn() => true;

    public Task<AdRulesResult> RunAsync(AdScriptWriteRequest request, string userPrompt, AdRulesCalls calls, CancellationToken ct) =>
        Task.FromResult<AdRulesResult>(new AdRulesResult.StillFlagged("raw", reason));

    public ValueTask AcceptedAsync(AdRulesAccepted accepted, CancellationToken ct) => ValueTask.CompletedTask;
}
