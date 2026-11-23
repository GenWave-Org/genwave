using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>Runs one ad reply through today's validator and through a rule on a snapshot the real <see cref="AdSnapshotSource"/> built.</summary>
internal static class AdParity
{
    public const string SponsorName = "Cravin's Diner";
    public const string SponsorPhone = "222-0100";

    static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.FromHours(-6));

    /// <summary>A 30 second spot, 40 percent tolerance, an owner sponsor with a phone on file, audience Everyone.</summary>
    public static AdScriptValidationRequest Request(
        AudiencePosture posture = AudiencePosture.Everyone, bool packOwned = false, string? phone = SponsorPhone) =>
        new(posture, 400, 30, 0.4, SponsorName, phone, packOwned);

    public static IPatterDurationEstimator Heuristic { get; } = new FixedDurationEstimator(TimeSpan.FromSeconds(5), PatterEstimateConfidence.Heuristic);

    public static TalkSnapshot Snapshot(string reply, AdScriptValidationRequest request, IPatterDurationEstimator estimator) =>
        new AdSnapshotSource(estimator)
            .BuildAsync(new AdSnapshotInput(reply, request, "GenWave", Now, "America/Edmonton", 1), CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();

    /// <summary>The rule id today's validator refuses the reply with (after the same hygiene the source applies), or null when it accepts.</summary>
    public static string? LegacyRuleId(string reply, AdScriptValidationRequest request, IPatterDurationEstimator estimator) =>
        AdScriptValidator.Validate(AdScriptWriter.ApplyLineAwareHygiene(reply), request, estimator)
            is AdScriptValidationResult.Refused { Violation.RuleId: var ruleId } ? ruleId : null;

    /// <summary>Per case: the validator's refusing rule id, then whether the rule flags the snapshot.</summary>
    public static (List<string?> Legacy, List<bool> Rule) Verdicts(
        IJudgingTalkRule rule, IReadOnlyList<AdParityCase> cases) =>
        ([.. cases.Select(c => LegacyRuleId(c.Reply, c.Request, c.Estimator))],
         [.. cases.Select(c => rule.Judge(Snapshot(c.Reply, c.Request, c.Estimator)) is not null)]);

    static readonly IJudgingTalkRule[] AdRules = [new FitsTimeRule(), new OtherBrandsRule(), new CleanLanguageRule(), new PhoneNumberRule()];

    /// <summary>How many of the four ad rules flag the case. A parity case must trip at most one, so today's first-refusal-wins validator cannot hide a second family.</summary>
    public static int FamiliesTripped(AdParityCase c)
    {
        var snapshot = Snapshot(c.Reply, c.Request, c.Estimator);
        return AdRules.Count(rule => rule.Judge(snapshot) is not null);
    }

    /// <summary>Exactly <paramref name="length"/> characters of plain two-letter words, ending on a letter, for length control.</summary>
    public static string Words(int length) => string.Concat(Enumerable.Repeat("so ", length)).Substring(0, length);
}
