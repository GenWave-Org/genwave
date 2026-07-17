namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{TOptions}"/> whose <see cref="CurrentValue"/> can be swapped
/// mid-test — enough for specs that read live options per call without standing up a real
/// configuration/DI pipeline. <see cref="OnChange"/> is unused by <c>LlmCopyWriter</c> (it re-reads
/// <see cref="CurrentValue"/> per render rather than subscribing) so it is a no-op here.
/// </summary>
public sealed class TestOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = initial;

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string> listener) => NoopDisposable.Instance;

    sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
