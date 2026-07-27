namespace GenWave.Host.Api;

/// <summary>
/// The wire projection of one <see cref="GenWave.Core.Domain.ScheduleCellError"/> — one entry of the
/// <c>cellErrors</c> array a <c>PUT /api/schedule</c> 400 carries (SPEC F91.1, F91.8; STORY-240,
/// PLAN T122). Named <c>cellErrors</c>, not <c>errors</c> — ASP.NET Core's automatic model-binding
/// 400 on this same endpoint+status already puts an object of string-arrays under <c>errors</c>, and
/// reusing that key would make the two 400 shapes indistinguishable without client-side type-sniffing.
/// Mirrors every field the domain type carries, field for field, so the caller (T129's
/// drag-paint editor) can map a rejection straight back onto the offending cell without re-deriving
/// anything: <see cref="RowIndex"/> indexes the submitted <c>segments</c> array; <see cref="Day"/>/
/// <see cref="StartMinute"/>/<see cref="EndMinute"/> are the same three wire fields that cell was
/// submitted with; <see cref="Kind"/> is the <see cref="GenWave.Core.Domain.ScheduleCellErrorKind"/>
/// enum spelled out as a boring camelCase string (mirrors <c>SettingsController</c>'s own
/// hand-mapped <c>kind</c>/<c>applyMode</c> wire strings rather than relying on
/// System.Text.Json's numeric default for an enum-typed property).
/// </summary>
public sealed record ScheduleCellErrorDto(
    int RowIndex,
    int Day,
    int StartMinute,
    int EndMinute,
    string Kind,
    string Message);
