namespace GenWave.PatterBench;

using System.Text.RegularExpressions;
using GenWave.Core.Domain;

/// <summary>
/// Loads the shipped persona cards with the same <see cref="PersonaCardSerializer"/> the app uses to read a
/// catalog persona.json. (The Host's import path wraps it with API checks; the bench needs only the card.)
/// </summary>
internal static partial class PersonaFixtures
{
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex SlugPattern();

    internal static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "personas");

    /// <summary>Null when the slug names no shipped card.</summary>
    internal static async Task<PersonaCard?> LoadAsync(string directory, string slug, CancellationToken ct)
    {
        if (!SlugPattern().IsMatch(slug))
        {
            return null;
        }

        var path = Path.Combine(directory, $"{slug}.persona.json");
        return File.Exists(path)
            ? PersonaCardSerializer.Deserialize(await File.ReadAllTextAsync(path, ct))
            : null;
    }
}
