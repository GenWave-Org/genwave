namespace GenWave.Core.Domain;

/// <summary>
/// Sparse patch applied to a media catalog row (STORY-039, Epic I).
/// Only present (non-null) fields are written; absent fields are left unchanged.
/// This is a partial-update shape — not a replacement. All tag fields are optional
/// so the caller supplies only the fields that changed.
///
/// <c>LibraryId</c> (STORY-048, Epic J): when present, reassigns the row to a different library.
/// If the destination library does not exist the write is rejected with 400.
/// If the destination is outside <c>Station:Scope:LibraryIds</c> the write still succeeds — the
/// track is parked in a non-rotating library — but the controller returns a warning via
/// response header <c>X-Out-Of-Scope: true</c> and body field <c>outOfScope: true</c>.
/// </summary>
public sealed record MediaPatch(
    string? Title,
    string? Artist,
    string? Album,
    string? Genre,
    int? Year,
    bool? Eligible,
    long? LibraryId);
