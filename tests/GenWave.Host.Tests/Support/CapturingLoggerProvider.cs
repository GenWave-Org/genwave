using Microsoft.Extensions.Logging;

namespace GenWave.Host.Tests.Support;

/// <summary>Captures every log entry of Warning or above so a spec can assert on a
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>'s own boot
/// narration — mirrors <c>Story164_FailClosedWithoutPassword.CapturingWarningLoggerProvider</c>'s own
/// idiom (that file's own remarks explain the shape). Hoisted here (PLAN T397 review fold) so
/// <see cref="PluginDoorWebFactory"/>'s own <c>Logs</c> property has a shared, non-<c>file</c>-scoped
/// home reachable from more than one spec file — the exact reason that factory itself moved here.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];
    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(formatter(state, exception));
        }
    }
}
