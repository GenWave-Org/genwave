namespace GenWave.Ads;

using System.Reflection;

/// <summary>
/// The GenWave release this process runs (gh-#865) — stamped onto a spot when the stale pass swaps
/// its take, so the ad page and the booth log can say "Re-rendered automatically on vX". Read from the
/// entry assembly's <see cref="AssemblyInformationalVersionAttribute"/>, which the release build sets
/// from the tag (<c>GW_VERSION</c>, e.g. <c>v5.13.1</c>). A <c>+commit</c> suffix is dropped, and a bare
/// number gets a leading <c>v</c>, so every stamp reads the same way.
/// </summary>
public sealed record AdAppVersion(string Value)
{
    public static AdAppVersion FromEntryAssembly() => From(
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static AdAppVersion From(string? informationalVersion)
    {
        var value = (informationalVersion ?? "").Split('+')[0].Trim();
        if (value.Length == 0)
            return new AdAppVersion("unknown");
        return new AdAppVersion(char.IsAsciiDigit(value[0]) ? $"v{value}" : value);
    }
}
