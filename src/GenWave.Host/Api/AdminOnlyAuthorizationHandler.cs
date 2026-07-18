using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// Handles <see cref="AdminOnlyRequirement"/>: succeeds for an authenticated cookie session, and —
/// T01's carry-over of the historical local-dev convenience — also succeeds when no
/// <c>Admin:Password</c> is configured at all (open mode).
///
/// STORY-164/T02 fail-closed change is exactly one line here: drop the <c>!passwordConfigured ||</c>
/// branch (plus add the startup warning) — this file is the only place that edit touches.
/// </summary>
sealed class AdminOnlyAuthorizationHandler(IOptionsMonitor<AdminOptions> adminOptions)
    : AuthorizationHandler<AdminOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminOnlyRequirement requirement)
    {
        var passwordConfigured = !string.IsNullOrEmpty(adminOptions.CurrentValue.Password);

        if (!passwordConfigured || context.User.Identity?.IsAuthenticated == true)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
