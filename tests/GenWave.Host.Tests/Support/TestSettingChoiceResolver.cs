using GenWave.Core.Abstractions;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Builds a real <see cref="SettingChoiceResolver"/> with zero registered <see cref="IChoiceProbe"/>s
/// over a throwaway <see cref="FakeIconPackStore"/>/<see cref="ThemeCatalog.LoadShipped"/>/
/// <see cref="TestSettingCopy.Real"/>, resolved through a cold <see cref="ProbedChoiceCache"/> — the
/// same style precedent as <see cref="TestSettingCopy.Real"/> itself. <paramref name="iconPackStore"/>/
/// <paramref name="logger"/> default to a throwaway <see cref="FakeIconPackStore"/>/<see cref="NullLogger{T}"/>
/// but can be swapped so a scenario proving the icon-pack degrade path (or asserting on the
/// resolver's own log output) does not have to hand-build a <see cref="SettingChoiceResolver"/>
/// itself (PLAN T580 review finding R3).
///
/// <see cref="GenWave.Host.Api.SettingsController"/>'s <c>choiceResolver</c> constructor parameter is
/// REQUIRED (PLAN T580 review finding F2: a dropped DI registration must fail loudly at activation,
/// never silently degrade to <see cref="ResolvedChoices.Failed"/> forever with nothing logged), so
/// every <c>new SettingsController(...)</c> unit test exercising some OTHER allowlisted key passes
/// <see cref="Default"/>() here. With zero probes registered, <c>Llm:Model</c>/<c>Station:Voice</c>
/// resolve to <see cref="ResolvedChoices.Failed"/> — the correct, defensive shape for "no probe backs
/// this key here" — while every other key's static/catalog choices resolve exactly as production
/// does.
/// </summary>
internal static class TestSettingChoiceResolver
{
    public static ISettingChoiceResolver Default(
        IIconPackStore? iconPackStore = null, ILogger<SettingChoiceResolver>? logger = null) => new SettingChoiceResolver(
        iconPackStore ?? new FakeIconPackStore(),
        ThemeCatalog.LoadShipped(),
        TestSettingCopy.Real(),
        [],
        [],
        new ProbedChoiceCache(TimeProvider.System, NullLogger<ProbedChoiceCache>.Instance),
        logger ?? NullLogger<SettingChoiceResolver>.Instance);
}
