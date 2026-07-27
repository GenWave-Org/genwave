namespace GenWave.Host.Api;

/// <summary>
/// One cell of the <c>GET/PUT /api/schedule</c> week document (SPEC F91.1, F91.8; STORY-240,
/// PLAN T122) — the wire projection of <see cref="GenWave.Core.Domain.ScheduleSegment"/>.
///
/// <para>
/// <see cref="Day"/> is a plain <c>int</c>, NOT the <see cref="System.DayOfWeek"/> enum: it carries
/// that enum's own 0-6 numbering (0 = Sunday) verbatim onto the wire, but deliberately as an
/// unvalidated integer — System.Text.Json's default enum handling would already accept an
/// out-of-range int without complaint, so typing this field as <c>int</c> just makes that fact
/// explicit rather than leaving it implicit in the JSON converter's behavior. An out-of-range value
/// is never rejected here; <c>ScheduleController</c> passes it straight through to
/// <see cref="GenWave.Core.Abstractions.IScheduleStore.ReplaceWeekAsync"/>, whose app-side
/// validation is what turns it into an <see cref="GenWave.Core.Domain.ScheduleCellErrorKind.InvalidDay"/>
/// per-cell 400 — the same "reject, don't 500" contract
/// <see cref="GenWave.MediaLibrary.Station.ScheduleRepository"/>'s own remarks document.
/// </para>
///
/// <para>
/// <see cref="Id"/> is populated on every row <c>GET /api/schedule</c> returns (a stored row always
/// has a store-assigned id) and is IGNORED entirely when this shape arrives in a
/// <c>PUT /api/schedule</c> request body — the store always treats a submitted week as brand new
/// rows (SPEC F91.8's atomic delete-then-insert), so a client echoing back the id it was given on
/// GET has no effect and is never required.
/// </para>
///
/// <para>
/// Absent-field posture: <see cref="StartMinute"/>/<see cref="EndMinute"/> default to 0 when the
/// property is missing from the JSON body — System.Text.Json's ordinary behavior for a non-nullable
/// numeric — and since PUT is full-replace, that 0 either fails the store's own end&gt;start/range
/// checks as a per-cell 400 or is written and echoed back as a visibly-different stored week; there
/// is no "absent means unchanged" carry-forward from whatever was previously stored.
/// </para>
/// </summary>
public sealed record ScheduleSegmentDto(
    long? Id,
    int Day,
    int StartMinute,
    int EndMinute,
    long? PersonaId,
    IReadOnlyList<string>? Genres,
    double? EnergyMin,
    double? EnergyMax);
