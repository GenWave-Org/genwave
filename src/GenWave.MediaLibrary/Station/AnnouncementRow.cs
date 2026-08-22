namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of one <c>station.announcement</c> row (SPEC F143, STORY-357, PLAN T337),
/// mapped by the globally-enabled <c>DefaultTypeMap.MatchNamesWithUnderscores</c> — the same
/// positional-record shape <see cref="PersonaTasteRow"/> uses (no array-typed column here, so
/// Dapper's stricter constructor-matching binds every column cleanly).
///
/// <para>
/// Every <see cref="AnnouncementState"/> <see cref="AnnouncementRepository"/> can reach reads through
/// this same shape (SPEC F143.2: the pipeline never deletes a row, so there is no "gone" case to model
/// separately) — <see cref="AnnouncementStateTypeHandler"/> maps <see cref="State"/> to/from the
/// column's own lowercase text. <see cref="ClaimedAt"/>/<see cref="AiredAt"/> are
/// <see langword="null"/> until their own transition stamps them; <see cref="DeclineReason"/> is
/// <see langword="null"/> outside <see cref="AnnouncementState.Declined"/> (the table's own comment on
/// that column). <see cref="Source"/> stays the column's own raw text rather than
/// <see cref="AnnouncementSource"/> — see that enum's own remarks for why.
/// </para>
/// </summary>
sealed record AnnouncementRow(
    long Id,
    string Message,
    bool Verbatim,
    string? RequestedVoice,
    string Source,
    AnnouncementState State,
    string? DeclineReason,
    int CollapseCount,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? ClaimedAt,
    DateTime? AiredAt,
    DateTime StateChangedAt);
