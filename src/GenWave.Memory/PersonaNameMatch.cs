using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>Maps the <c>about</c> the model wrote to a persona on the roster (SPEC F218.18).</summary>
public static class PersonaNameMatch
{
    /// <summary>
    /// The persona other than <paramref name="speakerId"/> that <paramref name="about"/> names: the full name,
    /// else a word of exactly one name. Null for <c>self</c>, blank, the speaker, an unknown or an ambiguous name;
    /// the claim is then the speaker's own.
    /// </summary>
    public static Persona? Other(string about, long speakerId, IReadOnlyList<Persona> roster)
    {
        if (string.IsNullOrWhiteSpace(about) || about.Equals("self", StringComparison.OrdinalIgnoreCase))
            return null;

        var others = roster.Where(p => p.Id != speakerId).ToList();
        var full = others.Where(p => p.Name.Equals(about, StringComparison.OrdinalIgnoreCase)).ToList();
        if (full.Count == 1)
            return full[0];

        var byWord = others
            .Where(p => p.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Equals(about, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return byWord.Count == 1 ? byWord[0] : null;
    }
}
