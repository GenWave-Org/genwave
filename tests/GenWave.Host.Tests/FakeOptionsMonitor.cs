using Microsoft.Extensions.Options;

namespace GenWave.Host.Tests;

/// <summary>
/// Shared mutable <see cref="IOptionsMonitor{T}"/> double: <see cref="CurrentValue"/> can be swapped
/// mid-test to simulate a live <c>PUT /api/settings</c> reload without standing up a real options
/// pipeline. Several spec files also define their own <c>file</c>-scoped copy of this exact shape
/// (a file-scoped type cannot cross files) — this shared version exists for specs that don't already
/// have one and don't need the isolation, e.g. <c>PlayHistoryService</c>/<c>Story139</c> tests wired
/// against <c>IOptionsMonitor&lt;AdminOptions&gt;</c>.
/// </summary>
sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
