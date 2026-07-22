// gh-#74 — Failed admin logins log caller identity (IP / forwarded headers)
//
// BDD specification — xUnit. During the 2026-07-21 demo triage, "Login failed: wrong admin
// password" carried zero caller context, so operator-vs-intruder could only be argued from
// topology. Every login outcome now logs remote IP, CF-Connecting-IP, and
// Cf-Access-Authenticated-User-Email — newline-stripped (log forging), "-" when absent so a
// LAN-path bypass of Access is visible as such.

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Auth;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

/// <summary>Captures formatted log lines so specs can assert on the rendered message.</summary>
file sealed class CapturingAuthLogger : ILogger<AuthController>
{
    public List<(LogLevel Level, string Message)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Lines.Add((logLevel, formatter(state, exception)));
}

public static class FeatureLoginCallerIdentityLogging
{
    static (AuthController Controller, List<(LogLevel Level, string Message)> Lines) BuildController(
        string configuredPassword)
    {
        var logger = new CapturingAuthLogger();
        var controller = new AuthController(
            Microsoft.Extensions.Options.Options.Create(new AdminOptions { Password = configuredPassword }),
            new FakeStationIdentityProvider(new StationIdentity("st-01", "GenWave", "af_heart")),
            logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, logger.Lines);
    }

    public sealed class ScenarioFailedLoginCarriesCallerContext
    {
        [Fact]
        public async Task WarnLineCarriesRemoteIpAndBothCloudflareHeaders()
        {
            // AC1 — a wrong password from a tunnel-fronted caller logs the XFF-corrected remote,
            //       the CF-Connecting-IP, and the Access-verified identity in the single warn line.
            var (controller, lines) = BuildController("right-password");
            controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("172.28.20.5");
            controller.HttpContext.Request.Headers["CF-Connecting-IP"] = "50.99.76.107";
            controller.HttpContext.Request.Headers["Cf-Access-Authenticated-User-Email"] = "dean@example.com";

            var result = await controller.Login(new LoginRequest { Password = "wrong-password" });

            Assert.IsType<UnauthorizedObjectResult>(result);
            var (level, message) = Assert.Single(lines);
            Assert.Equal(LogLevel.Warning, level);
            Assert.Contains("remote: 172.28.20.5", message, StringComparison.Ordinal);
            Assert.Contains("cf-connecting-ip: 50.99.76.107", message, StringComparison.Ordinal);
            Assert.Contains("access-user: dean@example.com", message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AbsentHeadersLogAsDashMarkingTheNonAccessPath()
        {
            // AC2 — a LAN caller (no CF headers) logs "-" for both, which is itself the signal
            //       that the request never transited Cloudflare Access.
            var (controller, lines) = BuildController("right-password");
            controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");

            await controller.Login(new LoginRequest { Password = "wrong-password" });

            var (_, message) = Assert.Single(lines);
            Assert.Contains("remote: 192.168.1.50", message, StringComparison.Ordinal);
            Assert.Contains("cf-connecting-ip: -", message, StringComparison.Ordinal);
            Assert.Contains("access-user: -", message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NewlinesInHeaderValuesCannotForgeLogEntries()
        {
            // AC3 — caller-controlled header values are newline-stripped before logging
            //       (CodeQL cs/log-forging): the rendered line stays a single line.
            var (controller, lines) = BuildController("right-password");
            controller.HttpContext.Request.Headers["Cf-Access-Authenticated-User-Email"] =
                "evil@example.com\nwarn: forged entry";

            await controller.Login(new LoginRequest { Password = "wrong-password" });

            var (_, message) = Assert.Single(lines);
            Assert.DoesNotContain('\n', message);
            Assert.Contains("evil@example.com warn: forged entry", message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task FailClosedNoPasswordPathAlsoCarriesCallerContext()
        {
            // AC4 — the SPEC F60.4 no-configured-password rejection logs the same caller context
            //       as a wrong-password rejection; an attacker probing a locked admin plane is
            //       exactly who this issue wants identified.
            var (controller, lines) = BuildController(configuredPassword: "");
            controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.9");

            var result = await controller.Login(new LoginRequest { Password = "anything" });

            Assert.IsType<UnauthorizedObjectResult>(result);
            var (level, message) = Assert.Single(lines);
            Assert.Equal(LogLevel.Warning, level);
            Assert.Contains("remote: 10.0.0.9", message, StringComparison.Ordinal);
        }
    }
}
