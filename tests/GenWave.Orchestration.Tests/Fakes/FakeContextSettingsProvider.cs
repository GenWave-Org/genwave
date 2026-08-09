using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration.Tests.Fakes;

/// <summary>
/// Mutable <see cref="IContextSettingsProvider"/> double (SPEC F107.2, STORY-297, PLAN T224) — mirrors
/// <c>GenWave.Context.Tests.Fakes.FakeContextSettingsProvider</c> one project over (this project has
/// no reference to <c>GenWave.Context</c>). An unconfigured key reads back <see cref="Disabled"/>,
/// the same "nothing configured yet" shape every pre-T226 construction site sees.
/// </summary>
sealed class FakeContextSettingsProvider : IContextSettingsProvider
{
    public static readonly ContextProviderSettings Disabled = new(false, 0, 0, null);

    readonly Dictionary<string, ContextProviderSettings> settings = new(StringComparer.Ordinal);

    public void Set(string key, ContextProviderSettings value) => settings[key] = value;

    public ContextProviderSettings For(string key) => settings.TryGetValue(key, out var value) ? value : Disabled;
}
