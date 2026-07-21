// STORY-164 — Fail-closed when no password is configured
//
// BDD specification — xUnit (SPEC F60.4, F60.5). Supersedes the historical open-dev-mode: with
// Admin:Password empty the AdminOnly plane denies everything, login can never succeed, and the
// host says so loudly at startup. Accepted deviation from PRD #A (MEMORY 2026-07-18).
// Red until PLAN T02.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>Captures every log entry of Warning or above so a spec can assert on startup output.</summary>
file sealed class CapturingWarningLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];
    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingWarningLoggerProvider owner) : ILogger
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

/// <summary>
/// Boots the host with Admin__Password UNSET (empty password) — the fail-closed case. Unlike every
/// other <c>WebApplicationFactory</c> in this suite, <c>Admin__Password</c> is still mutated through
/// the real process environment here rather than <c>ConfigureWebHost</c>'s <c>UseSetting</c>: this
/// spec's whole point is proving the plane is fail-closed when the password is truly ABSENT, and
/// <c>UseSetting</c>/<c>ConfigureHostConfiguration</c>'s injected value sits at a LOWER configuration
/// precedence than a real environment variable (verified empirically) — so it cannot force absence
/// if the box already happens to export <c>Admin__Password</c> (e.g. a dev shell that sourced
/// <c>.env</c> for compose). Explicitly nulling the real env var is the only mechanism that
/// guarantees true absence regardless of ambient state. This is the sole remaining
/// process-env-mutating factory in the suite (see <see cref="EnvVarMutatingWebFactoryCollection"/>),
/// so its three scenarios below staying in that collection still fully serializes it against itself
/// — nothing else in the suite mutates this key anymore, so there is no cross-class race left.
/// <c>ConnectionStrings:Library</c> carries no such absence requirement (it just needs A value), so
/// it uses the ordinary per-instance <c>UseSetting</c> mechanism like every other factory.
/// </summary>
file sealed class NoPasswordWebFactory(CapturingWarningLoggerProvider logs) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.AddSingleton<ILoggerProvider>(logs);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable("Admin__Password", null);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Admin__Password", prevAdmin);
        }
    }
}

public static class FeatureFailClosedWithoutPassword
{
    // ── HAPPY PATH ────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioStartupWarning
    {
        [Fact]
        public async Task HostWarnsThatTheAdminPlaneIsInaccessible()
        {
            var logs = new CapturingWarningLoggerProvider();
            await using var factory = new NoPasswordWebFactory(logs);
            _ = factory.CreateClient(); // force host build

            Assert.Contains(logs.Messages, m => m.Contains("ADMIN_PASSWORD", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioAdminPlaneDenies
    {
        [Fact]
        public async Task AdminEndpointDeniesWithoutAnyCookie()
        {
            var logs = new CapturingWarningLoggerProvider();
            await using var factory = new NoPasswordWebFactory(logs);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/status");

            Assert.True(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                $"/api/status returned {(int)response.StatusCode} — open dev mode must be gone (F60.4).");
        }
    }

    [Collection(EnvVarMutatingWebFactoryCollection.Name)]
    public sealed class ScenarioLoginAlwaysFails
    {
        [Fact]
        public async Task EmptyPasswordLoginDoesNotSucceed()
        {
            var logs = new CapturingWarningLoggerProvider();
            await using var factory = new NoPasswordWebFactory(logs);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = "" });

            Assert.False(login.IsSuccessStatusCode);
        }

        [Fact]
        public async Task NoSessionCookieIsEverIssued()
        {
            var logs = new CapturingWarningLoggerProvider();
            await using var factory = new NoPasswordWebFactory(logs);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = "anything" });

            Assert.False(login.Headers.Contains("Set-Cookie"));
        }
    }
}
