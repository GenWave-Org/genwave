namespace GenWave.Tts.Tests.Fakes;

using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IOptionsMonitor{TOptions}"/> whose <see cref="OnChange"/> subscription is genuinely
/// wired — unlike <see cref="TestOptionsMonitor{T}"/> (a deliberate no-op there, since its only
/// consumer re-reads <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> per call rather than
/// subscribing). <see cref="SpeechCorrectionProvider"/> is this project's one production
/// <c>OnChange</c> subscriber, so a spec proving its live-rebuild behavior needs a fake that can
/// actually raise the callback — <see cref="Change"/> does that, the same signal a real settings
/// write raises via <c>StationSettingsConfigurationProvider.Reload()</c> in production.
/// </summary>
public sealed class ChangeableOptionsMonitor<T> : IOptionsMonitor<T>
{
    Action<T, string>? listener;

    public ChangeableOptionsMonitor(T initial) => CurrentValue = initial;

    public T CurrentValue { get; private set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string> listener)
    {
        this.listener += listener;
        return NoopDisposable.Instance;
    }

    /// <summary>Raises the change callback with <paramref name="updated"/>, exactly as a real
    /// options reload would.</summary>
    public void Change(T updated)
    {
        CurrentValue = updated;
        listener?.Invoke(updated, string.Empty);
    }

    sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
