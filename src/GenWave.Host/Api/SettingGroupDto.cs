namespace GenWave.Host.Api;

/// <summary>
/// The admin UI section a <see cref="SettingDto"/> is grouped under (SPEC F205.3, STORY-477, PLAN
/// T574) — the wire projection of <see cref="Configuration.SettingGroup"/> plus its label resolved
/// for the request culture, so a client can render a section heading without carrying its own copy
/// of the enum-to-label mapping.
/// </summary>
/// <param name="Id">
/// The group's own wire id — <see cref="Configuration.SettingGroup"/>'s member name, lowercased
/// (e.g. <c>"sound"</c>) — matching the resx <c>Group.{id}.Label</c> naming convention
/// <see cref="Configuration.SettingCopy.GroupLabel"/> already resolves against
/// (<see cref="Configuration.SettingCopy.GroupId"/> is the one place that lowering happens).
/// </param>
/// <param name="Label">
/// The display label for <paramref name="Id"/> (e.g. <c>"Sound"</c>), resolved for the request
/// culture via <see cref="Configuration.SettingCopy.GroupLabel"/> — falls back to the enum member's
/// own name when the resx carries no entry for it (never empty).
/// </param>
public sealed record SettingGroupDto(string Id, string Label);
