namespace GenWave.Core.Domain;

/// <summary>
/// One offending row from a <see cref="ScheduleReplaceResult.ValidationFailed"/> outcome (SPEC F91.1,
/// F91.8; STORY-240, PLAN T118). <see cref="RowIndex"/> is the row's position in the submitted week
/// document — the primitive a future <c>PUT /api/schedule</c> handler (T122) needs to map a rejection
/// straight back onto the offending cell; <see cref="Day"/>/<see cref="StartMinute"/>/
/// <see cref="EndMinute"/> are carried alongside it so a caller never has to re-index back into its
/// own submission just to render a human-readable error.
/// </summary>
public sealed record ScheduleCellError(
    int RowIndex,
    DayOfWeek Day,
    int StartMinute,
    int EndMinute,
    ScheduleCellErrorKind Kind,
    string Message);
