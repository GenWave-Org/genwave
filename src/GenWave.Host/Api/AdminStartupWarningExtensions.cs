using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// Startup-time warning for the fail-closed admin gate (SPEC F60.4/STORY-164): when no
/// <c>Admin:Password</c> is configured, no admin endpoint is reachable and login can never
/// succeed. Logged through the built host's own DI logging pipeline — not a bootstrap logger —
/// so it reaches every registered <see cref="ILoggerProvider"/>, including test doubles wired via
/// <c>ConfigureTestServices</c>.
/// </summary>
static class AdminStartupWarningExtensions
{
    public static void WarnIfAdminPasswordMissing(this WebApplication app)
    {
        var password = app.Services.GetRequiredService<IOptions<AdminOptions>>().Value.Password;
        if (string.IsNullOrEmpty(password))
        {
            app.Logger.LogWarning(
                "ADMIN_PASSWORD is not set — the admin plane is fully locked down: no admin " +
                "endpoint is reachable and login can never succeed. Set ADMIN_PASSWORD to enable " +
                "admin access.");
        }
    }
}
