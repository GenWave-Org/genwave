namespace GenWave.Host.Configuration;

/// <summary>
/// The admin UI section an <see cref="AllowedSetting"/> is grouped under (SPEC F205.1, STORY-477,
/// PLAN T572). Every one of <see cref="StationSettingsAllowlist.All"/>'s entries carries exactly
/// one — a pure display/organization concern, unrelated to <see cref="SettingApplyMode"/>,
/// <see cref="SettingKind"/>, or <see cref="SettingValidator"/>.
/// </summary>
public enum SettingGroup
{
    Sound,
    Voice,
    Announcements,
    Sponsors,
    Library,
    Community,
    Station,
    System,
}
