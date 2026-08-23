using Microsoft.AspNetCore.Authorization;
using GenWave.Host.Auth;

namespace GenWave.Architecture.Tests.Fixtures.L9Probe;

/// <summary>L9 probe: a class-level <c>[Authorize(AuthenticationSchemes = ...)]</c> naming the real
/// AnnounceToken scheme — the exact T340 review hazard (a widened schemes list silently promoting the
/// HA token to full admin), reproduced here as a compiled fixture rather than exercised against
/// production reality.</summary>
[Authorize(AuthenticationSchemes = AnnounceTokenAuthenticationDefaults.SchemeName)]
public sealed class ViolatesFence
{
    public void Handle() { }
}
