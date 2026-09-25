namespace GenWave.Host.Configuration;

/// <summary>
/// One live choice source a <see cref="SettingChoiceSource.Catalog"/> setting resolves through (SPEC
/// F205.7, STORY-482, PLAN T585) — the catalog-kind counterpart to <see cref="IChoiceProbe"/>. Reads
/// an in-process, DI-registered store fresh on every request rather than a remote endpoint: no
/// <see cref="ProbedChoiceCache"/> in front of it (SPEC F205.7d — "reads the database on every
/// request"), no scope key, no 60 s freshness window or 2 s timeout, since none of those exist to
/// absorb network latency/flakiness a local store call does not have. Host-internal: <see cref="ShowChoiceCatalog"/>
/// is the only implementation this codebase ships, wired into <c>Program.cs</c> — not a public plugin
/// seam.
/// </summary>
internal interface IChoiceCatalog
{
    /// <summary>
    /// The <see cref="SettingChoiceSource.Catalog.Kind"/> this catalog answers for (e.g. the
    /// allowlist entry naming <c>Crosstalk:Shows</c> names <c>"shows"</c>) — <see cref="ChoiceSourceBootCheck"/>'s
    /// own composition-time check matches an allowlist entry's <see cref="SettingChoiceSource.Catalog.Kind"/>
    /// against every registered catalog's own value here.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Fetches the live choice list fresh — no cache (SPEC F205.7d), so a write through the backing
    /// store is visible on the very next request with nothing to invalidate. Lets any fault (a down
    /// DB connection, a malformed row) simply throw; <see cref="SettingChoiceResolver"/>, not this
    /// method, is what turns that into <see cref="ResolvedChoices.Failed"/> for that key only, never
    /// the whole request.
    /// </summary>
    Task<IReadOnlyList<SettingChoice>> ListAsync(CancellationToken ct);
}
