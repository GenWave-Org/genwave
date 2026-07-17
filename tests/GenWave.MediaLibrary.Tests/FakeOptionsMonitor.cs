using Microsoft.Extensions.Options;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// Minimal mutable <see cref="IOptionsMonitor{T}"/> double: <see cref="CurrentValue"/> can be swapped
/// mid-test to simulate a live <c>PUT /api/settings</c> reload without standing up a real options
/// pipeline (SPEC F44.2/F44.3, closes gitea-#197) — <c>ScanService</c>, <c>EnrichmentService</c>,
/// <c>FfmpegCueAnalyzer</c>, and <c>FfmpegEnergyAnalyzer</c> all read live now instead of a
/// boot-frozen <c>IOptions.Value</c> snapshot. Placed in the top-level <c>GenWave.MediaLibrary.Tests</c>
/// namespace (not nested under <c>.Fakes</c>) so every spec file can see it without an extra
/// <c>using</c> — mirrors <c>Harness</c>'s own placement.
/// </summary>
sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
