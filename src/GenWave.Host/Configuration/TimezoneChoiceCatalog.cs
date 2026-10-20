namespace GenWave.Host.Configuration;

/// <summary>
/// Live <see cref="IChoiceCatalog"/> for <c>Station:Timezone</c> (gh-#260) — the host's own zone list
/// (<see cref="TimeZoneInfo.GetSystemTimeZones()"/>, IANA ids on Linux), so the picker offers exactly
/// the ids <see cref="SettingValidator"/>'s <c>FindSystemTimeZoneById</c> check accepts. The first
/// choice is always <c>("", "Server clock")</c>, flagged <see cref="SettingChoice.IsDefault"/> — empty is
/// the honest "container's own clock" state (gh-#117), and carrying it as a real choice (the
/// <see cref="StationSettingsAllowlist.IconPackChoices"/> shape) keeps it pickable, so an operator can
/// go back to it. Labels carry the zone's offset right now, read through <see cref="TimeProvider"/>.
/// </summary>
internal sealed class TimezoneChoiceCatalog(TimeProvider timeProvider) : IChoiceCatalog
{
    /// <summary>The <see cref="StationSettingsAllowlist"/> <c>Station:Timezone</c> entry's own
    /// <see cref="SettingChoiceSource.Catalog.Kind"/>.</summary>
    public const string CatalogKind = "timezones";

    public string Kind => CatalogKind;

    public Task<IReadOnlyList<SettingChoice>> ListAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var choices = new List<SettingChoice> { new("", "Server clock", IsDefault: true) };
        choices.AddRange(TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(zone => zone.Id, StringComparer.Ordinal)
            .Select(zone => new SettingChoice(zone.Id, $"{zone.Id} (UTC{FormatOffset(zone.GetUtcOffset(now))})")));
        return Task.FromResult<IReadOnlyList<SettingChoice>>(choices);
    }

    static string FormatOffset(TimeSpan offset) =>
        $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
}
