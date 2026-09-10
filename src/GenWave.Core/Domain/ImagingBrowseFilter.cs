namespace GenWave.Core.Domain;

/// <summary>
/// PLAN T446 (STORY-427 AC1/AC2, SPEC F174.7) — the two-filter admin browse the wizard's music
/// picker rides: narrow to one <see cref="ImagingKind"/>, optionally further to one jingle asset
/// role. Lives here, on <see cref="Abstractions.IAdminMediaQuery"/>'s own new overload, rather
/// than as new members on <see cref="MediaQuery"/> itself — SPEC F176.4 pins
/// <c>GenWave.Abstractions</c> (where <see cref="MediaQuery"/> is declared) unchanged for this
/// release, so a filter this release adds cannot land there.
/// </summary>
/// <param name="Kind">The imaging kind every returned row must carry.</param>
/// <param name="JingleRole">
/// The storage token (<c>bed</c>/<c>sting</c>/<c>station_id</c>) db/45's CHECK admits on
/// <c>library.media.jingle_role</c>. The caller (<c>MediaController</c>) validates this against
/// that same closed set, and refuses it entirely unless <see cref="Kind"/> is
/// <see cref="ImagingKind.Jingle"/>, before ever constructing this record — by the time an
/// instance exists, a non-null value here is already trusted input, never a raw query-string
/// echo. Null narrows by <see cref="Kind"/> alone.
/// </param>
public sealed record ImagingBrowseFilter(ImagingKind Kind, string? JingleRole = null);
