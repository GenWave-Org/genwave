namespace GenWave.Core.TalkRules;

/// <summary>On/off per rule per kind of talk.</summary>
public interface ITalkRuleSwitches
{
    /// <summary>The stored switches for one kind, by rule id. A rule with no row is absent (its <c>OnByDefault</c> applies).</summary>
    ValueTask<IReadOnlyDictionary<string, bool>> GetAllAsync(TalkKinds kind, CancellationToken ct);

    ValueTask SetAsync(string ruleId, TalkKinds kind, bool enabled, CancellationToken ct);
}
