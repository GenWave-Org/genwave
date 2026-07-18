using Microsoft.AspNetCore.Authorization;

namespace GenWave.Host.Api;

/// <summary>
/// Marker requirement for the <see cref="AuthorizationPolicies.AdminOnly"/> policy. Carries no
/// state — this type only exists to give the policy a requirement for
/// <see cref="AdminOnlyAuthorizationHandler"/> to attach a handler to.
/// </summary>
sealed class AdminOnlyRequirement : IAuthorizationRequirement;
