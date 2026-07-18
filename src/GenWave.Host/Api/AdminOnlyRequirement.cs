using Microsoft.AspNetCore.Authorization;

namespace GenWave.Host.Api;

/// <summary>
/// Marker requirement for the <see cref="AuthorizationPolicies.AdminOnly"/> policy. Carries no
/// state — <see cref="AdminOnlyAuthorizationHandler"/> reads the live <c>Admin:Password</c> value
/// itself, so this type only exists to give the policy a requirement to attach a handler to.
/// </summary>
sealed class AdminOnlyRequirement : IAuthorizationRequirement;
