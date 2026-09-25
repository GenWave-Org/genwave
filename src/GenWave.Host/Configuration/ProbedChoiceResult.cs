namespace GenWave.Host.Configuration;

/// <summary>
/// The outcome of resolving one <see cref="IChoiceProbe"/> through <see cref="ProbedChoiceCache"/>
/// (SPEC F205.7b/c, STORY-479, PLAN T579) — a closed hierarchy, private base constructor plus sealed
/// record cases, mirroring <see cref="SettingChoiceSource"/>'s own shape for the same exhaustive-
/// switch guarantee at every call site.
/// </summary>
internal abstract record ProbedChoiceResult
{
    private ProbedChoiceResult() { }

    /// <summary>This attempt answered within its 2 s timeout: <paramref name="Choices"/> is that
    /// attempt's own list.</summary>
    public sealed record Fresh(IReadOnlyList<SettingChoice> Choices) : ProbedChoiceResult;

    /// <summary>This attempt failed, but an earlier attempt for the SAME
    /// <see cref="IChoiceProbe.ScopeKey"/> succeeded (SPEC F205.7c) — <paramref name="LastGood"/> is
    /// that earlier list; the DTO carries it with <c>choicesStale: true</c>.</summary>
    public sealed record Stale(IReadOnlyList<SettingChoice> LastGood) : ProbedChoiceResult;

    /// <summary>No attempt for the current <see cref="IChoiceProbe.ScopeKey"/> has ever succeeded —
    /// a fresh process, an always-down endpoint, or the endpoint just changed (SPEC F205.7c) — the
    /// DTO reports <c>choices: []</c>, <c>choicesFailed: true</c>.</summary>
    public sealed record Failed : ProbedChoiceResult
    {
        /// <summary>The singleton instance — <see cref="Failed"/> carries no data of its own.</summary>
        public static Failed Instance { get; } = new();
    }
}
