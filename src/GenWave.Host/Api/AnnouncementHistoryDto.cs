namespace GenWave.Host.Api;

/// <summary>
/// One row of the 200 body for <c>GET /api/announcements</c> (SPEC F146.2, STORY-361, PLAN T344) —
/// the F143.2 visible-decline/visible-expiry surface's own wire shape, projected straight off
/// <see cref="GenWave.Core.Domain.AnnouncementHistoryEntry"/> with no Host-only fields added. The
/// Announcements page's history list (F146.2) renders exactly this shape per row: state, decline
/// reason where present, collapse count, and the aired timestamp — a distinct type from that Core
/// record anyway (rather than serializing it directly), matching every other controller in this
/// namespace's own "Host owns its own wire DTOs" convention (see e.g. <see cref="AnnouncementAcceptedDto"/>).
/// </summary>
public sealed record AnnouncementHistoryDto(
    long Id,
    string Message,
    bool Verbatim,
    string State,
    string? DeclineReason,
    int CollapseCount,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? AiredAt);
