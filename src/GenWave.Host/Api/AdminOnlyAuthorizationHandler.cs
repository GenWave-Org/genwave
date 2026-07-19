using Microsoft.AspNetCore.Authorization;

namespace GenWave.Host.Api;

/// <summary>
/// Handles <see cref="AdminOnlyRequirement"/>: succeeds only for an authenticated cookie session.
///
/// STORY-164/T02 (fail-closed, SPEC F60.4): when no <c>Admin:Password</c> is configured, login can
/// never succeed (see <see cref="AuthController.Login"/>), so no cookie session ever exists — the
/// admin plane is fully denied rather than falling back to the historical local-dev open mode.
/// </summary>
sealed class AdminOnlyAuthorizationHandler : AuthorizationHandler<AdminOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdminOnlyRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
