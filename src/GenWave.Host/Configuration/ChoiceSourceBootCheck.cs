namespace GenWave.Host.Configuration;

/// <summary>
/// Composition-time fail-fast guard (SPEC F205.7, STORY-479, PLAN T580): an allowlist entry naming
/// a <see cref="SettingChoiceSource.Probe"/>/<see cref="SettingChoiceSource.Catalog"/> that no
/// registered probe/catalog answers for is a deploy-time bug — a typo'd name, a probe registration
/// dropped in a refactor — and must fail BOOT loudly, never degrade a live request to
/// <see cref="ResolvedChoices.Failed"/> the way <see cref="SettingChoiceResolver"/>'s own runtime
/// dispatch defensively does for the SAME misconfiguration (so pre-existing tests constructing a
/// resolver with zero registered probes keep passing). <c>Program.cs</c> calls <see cref="Verify"/>
/// once, right after <c>builder.Build()</c>, with <c>knownProbeNames</c> read back off the built
/// <see cref="IServiceProvider"/> (<c>app.Services.GetServices&lt;IChoiceProbe&gt;()</c>) rather than
/// a hardcoded name list (T580 review finding F3) — a static list would still boot clean if the
/// matching DI registration were the thing actually deleted, the exact drift this guard exists to
/// catch. Takes the allowlist as a parameter (rather than reading
/// <see cref="StationSettingsAllowlist.All"/> itself) so a test can prove the failure shape against
/// a deliberately misnamed entry without mutating the real allowlist.
/// </summary>
internal static class ChoiceSourceBootCheck
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> naming the first <paramref name="allowlist"/>
    /// entry whose <see cref="AllowedSetting.ChoiceSource"/> names a probe/catalog absent from
    /// <paramref name="knownProbeNames"/>/<paramref name="knownCatalogNames"/>.
    /// </summary>
    public static void Verify(
        IReadOnlyList<AllowedSetting> allowlist,
        IReadOnlyCollection<string> knownProbeNames,
        IReadOnlyCollection<string> knownCatalogNames)
    {
        foreach (var allowed in allowlist)
        {
            switch (allowed.ChoiceSource)
            {
                case SettingChoiceSource.Probe probe when !knownProbeNames.Contains(probe.Name):
                    throw new InvalidOperationException(
                        $"Setting '{allowed.Key}' names an unregistered choice probe '{probe.Name}'.");
                case SettingChoiceSource.Catalog catalog when !knownCatalogNames.Contains(catalog.Kind):
                    throw new InvalidOperationException(
                        $"Setting '{allowed.Key}' names an unregistered choice catalog '{catalog.Kind}'.");
            }
        }
    }
}
