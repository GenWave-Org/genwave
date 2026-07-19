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

/// <summary>Boots the host with Admin__Password UNSET (empty password) — the fail-closed case.</summary>
file sealed class NoPasswordWebFactory(CapturingWarningLoggerProvider logs) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
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
        var prevLib = Environment.GetEnvironmentVariable("ConnectionStrings__Library");
        var prevAdmin = Environment.GetEnvironmentVariable("Admin__Password");
        Environment.SetEnvironmentVariable("ConnectionStrings__Library", "Host=nowhere;Database=test");
        Environment.SetEnvironmentVariable("Admin__Password", null);
        try
        {
            return base.CreateHost(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Library", prevLib);
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
