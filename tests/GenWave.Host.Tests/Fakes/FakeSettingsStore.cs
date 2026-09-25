using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IStationSettingsStore"/> double — an allowlist-checked overlay, shared by
/// every <see cref="GenWave.Host.Api.SettingsController"/> spec that needs one (was duplicated
/// verbatim between Story124_EndpointLiveness.cs and Story479_LiveChoiceLists.cs; T580 review Note
/// 2 moved it here). <see cref="WriteCallCount"/> lets a fact assert a PUT reached the store exactly
/// once per key without depending on <see cref="ReadAllAsync"/>'s own shape.
/// </summary>
sealed class FakeSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCallCount { get; private set; }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not allowlisted.", nameof(key));
        overrides[key] = value?.ToString() ?? string.Empty;
        WriteCallCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> result =
            new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(result);
    }
}
