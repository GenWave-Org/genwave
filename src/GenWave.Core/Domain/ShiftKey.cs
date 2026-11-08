using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GenWave.Core.Domain;

/// <summary>
/// Identifies one DJ shift (gh-#293, SPEC F218): <c>personaId|day|startMinute|date</c> for a scheduled
/// show segment, <c>personaId|date</c> when no segment is on air. <c>date</c> is the station-local
/// calendar date (<c>yyyy-MM-dd</c>), so a shift crossing UTC midnight keeps one key. Pure value:
/// build with <see cref="Scheduled"/>/<see cref="Unscheduled"/>, round-trip via
/// <see cref="ToString"/>/<see cref="TryParse"/>.
/// </summary>
public sealed record ShiftKey
{
    const char Separator = '|';
    const string DateFormat = "yyyy-MM-dd";

    readonly string value;

    ShiftKey(string value) => this.value = value;

    public static ShiftKey Scheduled(long personaId, DayOfWeek day, int startMinute, DateOnly localDate)
    {
        if (startMinute is < 0 or >= 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(startMinute), startMinute, "Start minute must be within one day.");
        }

        return new(string.Create(CultureInfo.InvariantCulture,
            $"{personaId}{Separator}{(int)day}{Separator}{startMinute}{Separator}{Format(localDate)}"));
    }

    public static ShiftKey Unscheduled(long personaId, DateOnly localDate) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{personaId}{Separator}{Format(localDate)}"));

    public static bool TryParse(string? text, [NotNullWhen(true)] out ShiftKey? key)
    {
        key = null;
        var parts = text?.Split(Separator);
        var valid = parts switch
        {
            [var p, var d] => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _) && IsDate(d),
            [var p, var day, var start, var d] =>
                long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var dayNo) && dayNo is >= 0 and <= 6
                && int.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out var minute) && minute < 24 * 60
                && IsDate(d),
            _ => false,
        };

        if (valid && text is not null)
        {
            key = new ShiftKey(text);
        }

        return valid;
    }

    static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    static bool IsDate(string s) =>
        DateOnly.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public override string ToString() => value;
}
