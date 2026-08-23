namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/announcements</c> (SPEC F143.1). <see cref="Verbatim"/> defaults to
/// <see langword="false"/> (flavored) when omitted — the DDL's own column default (db/40);
/// <see cref="TtlSeconds"/> defaults to the store's own 900s when omitted, otherwise must fall inside
/// SPEC F143.1's 60–3600 bound (<see cref="AnnouncementsController"/>'s own job to enforce, never this
/// type's). <see cref="Voice"/> is carried through untouched — untrusted at this door, validated
/// against known voices only at RENDER time (PLAN T341/T342, the T337 review carry-forward).
/// </summary>
public sealed record AnnouncementRequest(string? Message, bool? Verbatim, int? TtlSeconds, string? Voice);
