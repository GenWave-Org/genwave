namespace GenWave.Core.Domain;

/// <summary>
/// Wire/storage tokens for <see cref="ImagingKind"/> (gh-#149): the snake_case strings the
/// <c>library.media.imaging_kind</c> check constraint admits and the admin API round-trips —
/// <c>liner</c>, <c>station_id</c>, <c>jingle</c>, <c>promo</c>. Mirrors
/// <c>PersonaTasteRepository.ToSourceText</c>'s enum↔token idiom.
/// </summary>
public static class ImagingKindTokens
{
    /// <summary>The token stored in <c>library.media.imaging_kind</c> for <paramref name="kind"/>.</summary>
    public static string ToToken(ImagingKind kind) => kind switch
    {
        ImagingKind.Liner     => "liner",
        ImagingKind.StationId => "station_id",
        ImagingKind.Jingle    => "jingle",
        ImagingKind.Promo     => "promo",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown imaging kind"),
    };

    /// <summary>
    /// Parses an operator-supplied kind. Null/blank resolves to <see cref="ImagingKind.Liner"/> —
    /// the gh-#149 default that preserves pre-kind behavior. Accepts the storage token
    /// (<c>station_id</c>) or the enum name (<c>StationId</c>), case-insensitively.
    /// </summary>
    public static bool TryParse(string? raw, out ImagingKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            kind = ImagingKind.Liner;
            return true;
        }

        switch (raw.Trim().ToLowerInvariant())
        {
            case "liner":
                kind = ImagingKind.Liner;
                return true;
            case "station_id":
            case "stationid":
                kind = ImagingKind.StationId;
                return true;
            case "jingle":
                kind = ImagingKind.Jingle;
                return true;
            case "promo":
                kind = ImagingKind.Promo;
                return true;
            default:
                kind = ImagingKind.Liner;
                return false;
        }
    }
}
