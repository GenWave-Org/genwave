namespace GenWave.Host.Api;

/// <summary>
/// A single key/value pair supplied in the body of <c>PUT /api/settings</c>.
/// </summary>
public sealed record SettingUpdateRequest(string Key, string Value);
