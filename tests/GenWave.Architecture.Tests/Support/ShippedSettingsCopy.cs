using System.Globalization;
using System.Resources;
using GenWave.Host.Configuration;

namespace GenWave.Architecture.Tests.Support;

/// <summary>Reads <see cref="SettingsResources"/>'s embedded neutral culture ResourceSet — what
/// actually SHIPS in GenWave.Host.dll — as a plain name/value map, rather than parsing
/// <c>SettingsResources.resx</c> as text (its schema-header XML comment contains sample
/// Name1/Color1/Bitmap1/Icon1 rows that a naive text/regex scan would miscount as real entries).</summary>
internal static class ShippedSettingsCopy
{
    public static IReadOnlyDictionary<string, string> Entries()
    {
        var manager = new ResourceManager(typeof(SettingsResources));
        var resourceSet = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException("SettingsResources has no neutral-culture ResourceSet.");

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in resourceSet)
            entries[(string)entry.Key] = entry.Value as string ?? string.Empty;
        return entries;
    }
}
