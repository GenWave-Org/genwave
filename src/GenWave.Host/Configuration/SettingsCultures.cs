using System.Globalization;

namespace GenWave.Host.Configuration;

/// <summary>
/// Discovers which cultures the settings-copy resx actually ships for (SPEC F205.2, STORY-477,
/// PLAN T573), by scanning a base directory for a satellite assembly carrying
/// <see cref="SettingsResources"/>'s own compiled resource (culture-suffixed satellite naming; the
/// resx file name matches the marker type's name, so the SDK default resource naming resolves it). Pulled out of
/// <c>Program.cs</c> so it can be exercised directly in tests instead of only through the whole
/// host's boot.
/// </summary>
public static class SettingsCultures
{
    /// <summary>
    /// Returns "en" plus every culture <paramref name="baseDirectory"/> ships a
    /// <c>{culture}/{SettingsResources assembly}.resources.dll</c> satellite for. "en" is always
    /// included as the floor, even with no satellite present, so a fresh deploy that has never had a
    /// second culture added still resolves a default request culture correctly. Looking for THIS
    /// assembly's own satellite file (not just "some culture directory exists") avoids false
    /// positives from a framework/NuGet dependency that ships its OWN satellite resources for many
    /// cultures the settings resx has never been translated into. A directory name that fails to
    /// parse as a culture (an unrelated folder under the publish output) is skipped rather than
    /// failing boot. A missing <paramref name="baseDirectory"/> yields just the "en" floor. A culture
    /// already in the result (an explicit <c>en/</c> satellite directory, however unusual, would
    /// otherwise duplicate the "en" floor already added above) is skipped rather than added twice.
    /// </summary>
    public static IReadOnlyList<CultureInfo> Discover(string baseDirectory)
    {
        var cultures = new List<CultureInfo> { CultureInfo.GetCultureInfo("en") };
        var seenCultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };
        var satelliteResourceFile = $"{typeof(SettingsResources).Assembly.GetName().Name}.resources.dll";
        var directory = new DirectoryInfo(baseDirectory);
        if (!directory.Exists)
            return cultures;

        foreach (var cultureDirectory in directory.EnumerateDirectories())
        {
            if (!File.Exists(Path.Combine(cultureDirectory.FullName, satelliteResourceFile)))
                continue;

            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureDirectory.Name);
            }
            catch (CultureNotFoundException)
            {
                // Not a culture-named directory — ignore.
                continue;
            }

            if (seenCultureNames.Add(culture.Name))
                cultures.Add(culture);
        }

        return cultures;
    }
}
