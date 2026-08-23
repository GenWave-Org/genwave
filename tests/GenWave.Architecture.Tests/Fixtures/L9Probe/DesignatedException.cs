using Microsoft.AspNetCore.Authorization;
using GenWave.Host.Auth;

namespace GenWave.Architecture.Tests.Fixtures.L9Probe;

/// <summary>L9 probe: carries the SAME hazard shape as <see cref="ViolatesFence"/>, but stands in for
/// the one designated type SPEC F145.3/.4 grants the scheme to — the probe-only scenario passes THIS
/// type's own full name as the "designated" argument (never the real
/// <c>GenWave.Host.Api.AnnouncementsController</c>), proving the exclusion is a genuine parameter of
/// the detector, not a hardcoded assumption baked into it.</summary>
[Authorize(AuthenticationSchemes = AnnounceTokenAuthenticationDefaults.SchemeName)]
public sealed class DesignatedException
{
    public void Handle() { }
}
