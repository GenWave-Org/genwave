using Microsoft.AspNetCore.Authorization;

namespace GenWave.Architecture.Tests.Fixtures.L9Probe;

/// <summary>L9 probe: an ordinary policy-only <c>[Authorize]</c> (no <c>AuthenticationSchemes</c> at
/// all — the shape every admin controller besides the two announcement ones already carries) plus a
/// scheme name that merely CONTAINS the real scheme name as a substring
/// (<c>"AnnounceTokenExtra"</c>) — must never be flagged: the detector's own remarks name comma-split
/// exact matching as the reason a substring hit like this stays clean.</summary>
[Authorize(Policy = "SomeOtherPolicy")]
public sealed class StaysClean
{
    [Authorize(AuthenticationSchemes = "AnnounceTokenExtra")]
    public void LooksSimilarButIsNotTheSameScheme() { }
}
