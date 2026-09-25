using GenWave.Core.Abstractions;
using GenWave.Host.Theming;

namespace GenWave.Host.Configuration;

/// <summary>
/// Default <see cref="ISettingChoiceResolver"/> (SPEC F205.7, STORY-479, PLAN T580). The
/// <see cref="SettingChoiceSource.Static"/> arm is <see cref="GenWave.Host.Api.SettingsController"/>'s
/// own former <c>ChoicesFor</c>/<c>LocalizedChoicesFor</c> logic, moved here unchanged —
/// <c>Station:Theme</c> widens to the live <see cref="ThemeCatalog"/>, <c>Station:IconPack</c> to a
/// once-per-request <see cref="IIconPackStore"/> fetch, made only when <c>Station:IconPack</c> is among the resolved keys (degrading to house-icons-only on a
/// transient outage, same as before), everything else is the entry's own frozen
/// <see cref="AllowedSetting.Choices"/>. The <see cref="SettingChoiceSource.Probe"/> arm is new:
/// every such entry resolves in parallel (<see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/>)
/// through the shared <see cref="ProbedChoiceCache"/>/<see cref="IChoiceProbe"/> (STORY-479, PLAN
/// T579) — each probe's own 60 s cache and 2 s timeout already bound the cost of ONE entry; this
/// resolver adds nothing sequential on top of that for a page with several.
///
/// <para>
/// No <see cref="SettingChoiceSource.Catalog"/> source is registered yet — nothing on
/// <see cref="StationSettingsAllowlist"/> uses it (T580's own scope is the two Probe entries,
/// <c>Llm:Model</c>/<c>Station:Voice</c>). An entry naming one anyway resolves defensively to
/// <see cref="ResolvedChoices.Failed"/>, the SAME posture as a Probe entry naming an unregistered
/// probe — <see cref="ChoiceSourceBootCheck"/>, not this resolver, is what fails the DEPLOY for
/// either misconfiguration; this method must never crash a request over it.
/// </para>
///
/// <para>
/// Only resolves the <see cref="AllowedSetting.Kind"/>-<see cref="SettingKind.Choice"/> entries
/// actually PRESENT in <paramref name="currentValues"/> (T580 review finding F4) — <c>GET</c>
/// passes every allowlisted key (so every choice-kind entry resolves), <c>PUT</c> passes only the
/// keys just written. Resolving the full allowlist regardless of what was asked meant a
/// <c>PUT Station:Name</c> with a cold cache and a down LLM/TTS backend waited on probe attempts
/// it was about to throw away.
/// </para>
///
/// <para>
/// A saved value missing from the resolved list is never rejected by <see cref="SettingValidator"/>
/// (SPEC F205.7a — a live source can legitimately drop a model/voice the operator once picked) but
/// IS still shown: appended to the returned list labelled via the resx-driven
/// <see cref="SettingCopy.NotFoundLabel"/>, so the admin UI's dropdown always contains the value it
/// is currently displaying.
/// </para>
/// </summary>
internal sealed class SettingChoiceResolver(
    IIconPackStore iconPackStore,
    ThemeCatalog themeCatalog,
    SettingCopy settingCopy,
    IEnumerable<IChoiceProbe> probes,
    ProbedChoiceCache probeCache,
    ILogger<SettingChoiceResolver> logger) : ISettingChoiceResolver
{
    public async Task<IReadOnlyDictionary<string, ResolvedChoices>> ResolveAsync(
        IReadOnlyDictionary<string, string> currentValues, CancellationToken ct)
    {
        var choiceEntries = currentValues.Keys
            .Select(key => StationSettingsAllowlist.ByKey.TryGetValue(key, out var allowed) ? allowed : null)
            .OfType<AllowedSetting>()
            .Where(a => a.Kind == SettingKind.Choice)
            .ToList();
        if (choiceEntries.Count == 0)
            return new Dictionary<string, ResolvedChoices>(StringComparer.OrdinalIgnoreCase);

        var iconPackChoices = choiceEntries.Any(entry => entry.Key == "Station:IconPack")
            ? await IconPackChoicesAsync(ct).ConfigureAwait(false)
            : [];
        var probesByName = probes.ToDictionary(p => p.Name, StringComparer.Ordinal);

        var resolved = await Task.WhenAll(
            choiceEntries.Select(allowed =>
                ResolveEntryAsync(allowed, iconPackChoices, probesByName, currentValues, ct))).ConfigureAwait(false);

        return resolved.ToDictionary(entry => entry.Key, entry => entry.Result, StringComparer.OrdinalIgnoreCase);
    }

    async Task<(string Key, ResolvedChoices Result)> ResolveEntryAsync(
        AllowedSetting allowed,
        IReadOnlyList<SettingChoice> iconPackChoices,
        IReadOnlyDictionary<string, IChoiceProbe> probesByName,
        IReadOnlyDictionary<string, string> currentValues,
        CancellationToken ct)
    {
        var resolved = allowed.ChoiceSource switch
        {
            SettingChoiceSource.Probe probeSource =>
                await ResolveProbeAsync(probeSource, probesByName, ct).ConfigureAwait(false),
            SettingChoiceSource.Catalog catalogSource => ResolveUnregisteredCatalog(catalogSource),
            _ => new ResolvedChoices(StaticChoicesFor(allowed, iconPackChoices)),
        };

        var localized = Localize(allowed.Key, resolved);
        var withSaved = AppendSavedIfMissing(allowed.Key, localized, currentValues);
        return (allowed.Key, withSaved);
    }

    async Task<ResolvedChoices> ResolveProbeAsync(
        SettingChoiceSource.Probe probeSource, IReadOnlyDictionary<string, IChoiceProbe> probesByName, CancellationToken ct)
    {
        if (!probesByName.TryGetValue(probeSource.Name, out var probe))
        {
            logger.LogWarning("No IChoiceProbe registered named {ProbeName}", probeSource.Name);
            return new ResolvedChoices([], Failed: true);
        }

        var result = await probeCache.GetAsync(probe, ct).ConfigureAwait(false);
        return result switch
        {
            ProbedChoiceResult.Fresh fresh => new ResolvedChoices(fresh.Choices),
            ProbedChoiceResult.Stale stale => new ResolvedChoices(stale.LastGood, Stale: true),
            ProbedChoiceResult.Failed => new ResolvedChoices([], Failed: true),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown ProbedChoiceResult case"),
        };
    }

    ResolvedChoices ResolveUnregisteredCatalog(SettingChoiceSource.Catalog catalogSource)
    {
        logger.LogWarning("No IChoiceCatalog registered named {CatalogKind}", catalogSource.Kind);
        return new ResolvedChoices([], Failed: true);
    }

    IReadOnlyList<SettingChoice> StaticChoicesFor(AllowedSetting allowed, IReadOnlyList<SettingChoice> iconPackChoices) =>
        allowed.Key switch
        {
            "Station:Theme" => StationSettingsAllowlist.ThemeChoices(themeCatalog),
            "Station:IconPack" => iconPackChoices,
            _ => allowed.Choices ?? [],
        };

    ResolvedChoices Localize(string key, ResolvedChoices resolved) =>
        resolved with
        {
            Choices = resolved.Choices
                .Select(choice => choice with { Label = settingCopy.TryChoiceLabel(key, choice.Value) ?? choice.Label })
                .ToList(),
        };

    ResolvedChoices AppendSavedIfMissing(string key, ResolvedChoices resolved, IReadOnlyDictionary<string, string> currentValues)
    {
        if (!currentValues.TryGetValue(key, out var savedValue) || string.IsNullOrEmpty(savedValue))
            return resolved;

        if (resolved.Choices.Any(choice => string.Equals(choice.Value, savedValue, StringComparison.Ordinal)))
            return resolved;

        var appended = resolved.Choices.Append(new SettingChoice(savedValue, settingCopy.NotFoundLabel(savedValue))).ToList();
        return resolved with { Choices = appended };
    }

    /// <summary>Moved verbatim from <c>SettingsController.IconPackChoicesAsync</c> (PLAN T303) — see
    /// that method's former remarks for why <see cref="iconPackStore"/> is a required constructor
    /// dependency while its own failure here degrades <c>Station:IconPack</c> to house-icons-only
    /// rather than failing the whole request.</summary>
    async Task<IReadOnlyList<SettingChoice>> IconPackChoicesAsync(CancellationToken ct)
    {
        try
        {
            return StationSettingsAllowlist.IconPackChoices(await iconPackStore.GetAllSlugsAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Icon pack listing unavailable for Station:IconPack's own choices — degrading to house icons only");
            return StationSettingsAllowlist.IconPackChoices([]);
        }
    }
}
