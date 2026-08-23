using Microsoft.AspNetCore.Authorization;
using GenWave.Host.Auth;

namespace GenWave.Architecture.Tests.Fixtures.L9Probe;

/// <summary>L9 probe: no class-level hazard, but one ACTION carries the real
/// <c>AnnounceTokenAuthenticationDefaults.InScopeSchemes</c> list (the comma-separated
/// "Cookie,AnnounceToken" shape a real controller action would use) — proves the detector reaches
/// method-level <c>[Authorize]</c>, not only the class-level attribute the shipped controllers happen
/// to use today.</summary>
public sealed class ViolatesFenceAtMethodLevel
{
    public void Clean() { }

    [Authorize(AuthenticationSchemes = AnnounceTokenAuthenticationDefaults.InScopeSchemes)]
    public void Hazard() { }
}
