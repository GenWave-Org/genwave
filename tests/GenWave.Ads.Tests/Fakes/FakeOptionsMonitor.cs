using Microsoft.Extensions.Options;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>Minimal <see cref="IOptionsMonitor{TOptions}"/> that always returns a mutable current
/// value — the Story080 <c>FakeOptionsMonitor</c> shape, made settable (not just constructor-fixed)
/// so a scenario can flip a live-shaped knob (e.g. <see cref="AdSpotAntiRepeatOptions.AntiRepeatWindow"/>)
/// mid-test without rebuilding the source under test.</summary>
public sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
