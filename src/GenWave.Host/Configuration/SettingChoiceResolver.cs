using System.Text.Json;
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
/// The <see cref="SettingChoiceSource.Catalog"/> arm (SPEC F205.7d, STORY-482, PLAN T585) dispatches
/// by <see cref="SettingChoiceSource.Catalog.Kind"/> through a DI-registered <see cref="IChoiceCatalog"/>
/// — <see cref="ShowChoiceCatalog"/> (<c>"shows"</c>) is the one this codebase ships, backing
/// <c>Crosstalk:Shows</c>' <see cref="SettingKind.MultiChoice"/> entry. No cache in front of it
/// (unlike <see cref="SettingChoiceSource.Probe"/>'s <see cref="ProbedChoiceCache"/>): a catalog reads
/// an in-process store, not a remote endpoint, fresh on every request. An entry naming an
/// unregistered kind resolves defensively to <see cref="ResolvedChoices.Failed"/>, the SAME posture a
/// <see cref="SettingChoiceSource.Probe"/> entry naming an unregistered probe already falls back to —
/// <see cref="ChoiceSourceBootCheck"/>, not this resolver, is what fails the DEPLOY for either
/// misconfiguration; this method must never crash a request over it.
/// </para>
///
/// <para>
/// Only resolves the <see cref="AllowedSetting.Kind"/>-<see cref="SettingKind.Choice"/>/
/// <see cref="SettingKind.MultiChoice"/> entries actually PRESENT in <paramref name="currentValues"/>
/// (T580 review finding F4) — <c>GET</c> passes every allowlisted key (so every choice-kind entry
/// resolves), <c>PUT</c> passes only the keys just written. Resolving the full allowlist regardless
/// of what was asked meant a <c>PUT Station:Name</c> with a cold cache and a down LLM/TTS backend
/// waited on probe attempts it was about to throw away.
/// </para>
///
/// <para>
/// A saved value missing from the resolved list is never rejected by <see cref="SettingValidator"/>
/// (SPEC F205.7a — a live source can legitimately drop a model/voice, or a show, the operator once
/// picked) but IS still shown: appended to the returned list labelled via the resx-driven
/// <see cref="SettingCopy.NotFoundLabel"/>, so the admin UI's control always contains every value it
/// is currently displaying. For <see cref="SettingKind.MultiChoice"/>, the saved value is a JSON
/// array (SPEC F205.7e) — every slug in it missing from the resolved list is appended, not just one.
/// </para>
/// </summary>
internal sealed class SettingChoiceResolver(
    IIconPackStore iconPackStore,
    ThemeCatalog themeCatalog,
    SettingCopy settingCopy,
    IEnumerable<IChoiceProbe> probes,
    IEnumerable<IChoiceCatalog> catalogs,
    ProbedChoiceCache probeCache,
    ILogger<SettingChoiceResolver> logger) : ISettingChoiceResolver
{
    public async Task<IReadOnlyDictionary<string, ResolvedChoices>> ResolveAsync(
        IReadOnlyDictionary<string, string> currentValues, CancellationToken ct)
    {
        var choiceEntries = currentValues.Keys
            .Select(key => StationSettingsAllowlist.ByKey.TryGetValue(key, out var allowed) ? allowed : null)
            .OfType<AllowedSetting>()
            .Where(a => a.Kind is SettingKind.Choice or SettingKind.MultiChoice)
            .ToList();
        if (choiceEntries.Count == 0)
            return new Dictionary<string, ResolvedChoices>(StringComparer.OrdinalIgnoreCase);

        var iconPackChoices = choiceEntries.Any(entry => entry.Key == "Station:IconPack")
            ? await IconPackChoicesAsync(ct).ConfigureAwait(false)
            : [];
        var probesByName = probes.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var catalogsByKind = catalogs.ToDictionary(c => c.Kind, StringComparer.Ordinal);

        var resolved = await Task.WhenAll(
            choiceEntries.Select(allowed =>
                ResolveEntryAsync(allowed, iconPackChoices, probesByName, catalogsByKind, currentValues, ct)))
            .ConfigureAwait(false);

        return resolved.ToDictionary(entry => entry.Key, entry => entry.Result, StringComparer.OrdinalIgnoreCase);
    }

    async Task<(string Key, ResolvedChoices Result)> ResolveEntryAsync(
        AllowedSetting allowed,
        IReadOnlyList<SettingChoice> iconPackChoices,
        IReadOnlyDictionary<string, IChoiceProbe> probesByName,
        IReadOnlyDictionary<string, IChoiceCatalog> catalogsByKind,
        IReadOnlyDictionary<string, string> currentValues,
        CancellationToken ct)
    {
        var resolved = allowed.ChoiceSource switch
        {
            SettingChoiceSource.Probe probeSource =>
                await ResolveProbeAsync(probeSource, probesByName, ct).ConfigureAwait(false),
            SettingChoiceSource.Catalog catalogSource =>
                await ResolveCatalogAsync(catalogSource, catalogsByKind, ct).ConfigureAwait(false),
            _ => new ResolvedChoices(StaticChoicesFor(allowed, iconPackChoices)),
        };

        var localized = Localize(allowed.Key, resolved);
        var withSaved = AppendSavedIfMissing(allowed, localized, currentValues);
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

    /// <summary>The <see cref="SettingChoiceSource.Catalog"/> arm (SPEC F205.7d, STORY-482, PLAN
    /// T585): an unregistered <paramref name="catalogSource"/> kind degrades to
    /// <see cref="ResolvedChoices.Failed"/> the same defensive way <see cref="ResolveProbeAsync"/>'s
    /// own unregistered-probe branch does — <see cref="ChoiceSourceBootCheck"/> is what actually fails
    /// the deploy for that misconfiguration. A registered catalog's own <see cref="IChoiceCatalog.ListAsync"/>
    /// fault (a down DB connection, a malformed row) is caught here rather than left to propagate,
    /// since one key's live source failing must never fail the whole <c>GET</c>/<c>PUT</c> (mirrors
    /// <see cref="IconPackChoicesAsync"/>'s own catch, one arm over) — the message template carries only
    /// the catalog kind and exception TYPE, never <c>ex.Message</c> as a structured property (a raw DB
    /// driver message is free text); the exception object is attached as usual. The guard is
    /// <c>!(ex is OperationCanceledException &amp;&amp; ct.IsCancellationRequested)</c>, not a bare
    /// <c>ex is not OperationCanceledException</c> (STORY-482, PLAN T585 review finding 2 — matches
    /// <see cref="ProbedChoiceCache.RefreshAsync"/>'s own two-catch split): a store-internal OCE (e.g.
    /// its own unrelated timeout) that fires while OUR caller's <paramref name="ct"/> was never
    /// cancelled is still a catalog failure to degrade, not a fault to let propagate and fail the
    /// whole request — only the CALLER's own cancellation may propagate uncaught.</summary>
    async Task<ResolvedChoices> ResolveCatalogAsync(
        SettingChoiceSource.Catalog catalogSource, IReadOnlyDictionary<string, IChoiceCatalog> catalogsByKind, CancellationToken ct)
    {
        if (!catalogsByKind.TryGetValue(catalogSource.Kind, out var catalog))
        {
            logger.LogWarning("No IChoiceCatalog registered named {CatalogKind}", catalogSource.Kind);
            return new ResolvedChoices([], Failed: true);
        }

        try
        {
            var choices = await catalog.ListAsync(ct).ConfigureAwait(false);
            return new ResolvedChoices(choices);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            logger.LogWarning(
                ex, "Choice catalog {CatalogKind} listing failed with {ExceptionType}",
                catalogSource.Kind, ex.GetType().Name);
            return new ResolvedChoices([], Failed: true);
        }
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

    ResolvedChoices AppendSavedIfMissing(AllowedSetting allowed, ResolvedChoices resolved, IReadOnlyDictionary<string, string> currentValues)
    {
        if (!currentValues.TryGetValue(allowed.Key, out var savedValue) || string.IsNullOrEmpty(savedValue))
            return resolved;

        if (allowed.Kind != SettingKind.MultiChoice)
            return AppendMissing(resolved, [savedValue]);

        string?[] savedSlugs;
        try
        {
            savedSlugs = JsonSerializer.Deserialize<string?[]>(savedValue) ?? [];
        }
        catch (JsonException)
        {
            // Not a JSON array of strings — SettingValidator's own per-key shape check already guards
            // the only shape a PUT can ever persist here; a malformed row reaching this method some
            // other way (a hand-edited DB row) degrades to "no extra choices shown" rather than
            // throwing mid-request (SPEC F205.7e).
            return resolved;
        }

        return AppendMissing(resolved, savedSlugs);
    }

    /// <summary>Appends whichever entries of <paramref name="saved"/> are missing from
    /// <paramref name="resolved"/>, labelled via <see cref="SettingCopy.NotFoundLabel"/> — shared by
    /// both a single <see cref="SettingKind.Choice"/> value (the caller passes a one-element array) and
    /// a <see cref="SettingKind.MultiChoice"/>'s whole saved slug array (STORY-482, PLAN T585 review
    /// finding 1). <paramref name="saved"/> is filtered to non-blank values BEFORE the membership test
    /// and de-duplicated (<see cref="StringComparer.Ordinal"/>, first-occurrence order preserved) —
    /// SPEC F205.7e's "A blank value is never appended" applies regardless of source, and a slug
    /// repeated in the saved array (a hand-edited or historical row) must still surface as exactly ONE
    /// "not found" choice, not one per repetition.</summary>
    ResolvedChoices AppendMissing(ResolvedChoices resolved, IEnumerable<string?> saved)
    {
        var missing = saved
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(value => !resolved.Choices.Any(choice => string.Equals(choice.Value, value, StringComparison.Ordinal)))
            .ToList();
        if (missing.Count == 0)
            return resolved;

        var appended = resolved.Choices
            .Concat(missing.Select(value => new SettingChoice(value, settingCopy.NotFoundLabel(value))))
            .ToList();
        return resolved with { Choices = appended };
    }

    /// <summary>Moved verbatim from <c>SettingsController.IconPackChoicesAsync</c> (PLAN T303) — see
    /// that method's former remarks for why <see cref="iconPackStore"/> is a required constructor
    /// dependency while its own failure here degrades <c>Station:IconPack</c> to house-icons-only
    /// rather than failing the whole request. The guard mirrors <see cref="ResolveCatalogAsync"/>'s own
    /// (STORY-482, PLAN T585 review finding 2) — only the CALLER's own <paramref name="ct"/>
    /// cancellation may propagate uncaught; a store-internal OCE degrades to house-icons-only same as
    /// any other fault.</summary>
    async Task<IReadOnlyList<SettingChoice>> IconPackChoicesAsync(CancellationToken ct)
    {
        try
        {
            return StationSettingsAllowlist.IconPackChoices(await iconPackStore.GetAllSlugsAsync(ct));
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Icon pack listing unavailable for Station:IconPack's own choices — degrading to house icons only");
            return StationSettingsAllowlist.IconPackChoices([]);
        }
    }
}
