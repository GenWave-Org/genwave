namespace GenWave.Context.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Mutable <see cref="IContextSettingsProvider"/> double (mirrors
/// <c>GenWave.Orchestration.Tests.Fakes.FakeCadenceProvider</c>/<c>FakeRotationSettingsProvider</c>
/// one seam over): per-key settings set explicitly with <see cref="Set"/>. Any key never set resolves
/// to <see cref="Disabled"/> — the same no-op/disabled stand-in T226's real
/// <c>IOptionsMonitor</c>-backed implementation replaces, so a test never needs to register every
/// provider it constructs.
/// </summary>
sealed class FakeContextSettingsProvider : IContextSettingsProvider
{
    public static readonly ContextProviderSettings Disabled = new(false, 0, 0, null);

    readonly Dictionary<string, ContextProviderSettings> settings = new(StringComparer.Ordinal);

    public void Set(string key, ContextProviderSettings value) => settings[key] = value;

    public ContextProviderSettings For(string key) => settings.TryGetValue(key, out var value) ? value : Disabled;
}
