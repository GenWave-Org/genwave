using System.Text.RegularExpressions;

namespace GenWave.Memory;

/// <summary>
/// Phrases a shared outing (SPEC F218.18) from each side. The speaker keeps the claim as the model wrote it
/// (no rewrite). The other persona gets the same claim with their own name swapped for the speaker's: the whole name first, else any
/// word of it (so "Flip" in the claim matches "Captain Flip"). A claim that never names them gets
/// <c>, with {speaker}</c> appended. A rewrite rule, not a second model call, so each side's row is predictable.
/// </summary>
public static class OutingPhrasing
{
    /// <summary>The row the other persona keeps.</summary>
    public static string ForOther(string claim, string otherName, string speakerName)
    {
        var swapped = Swap(claim, otherName, speakerName);
        if (swapped is not null)
            return swapped;

        foreach (var word in otherName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            swapped = Swap(claim, word, speakerName);
            if (swapped is not null)
                return swapped;
        }

        return $"{claim}, with {speakerName}";
    }

    /// <summary>A one-line neutral description of the event both rows point at.</summary>
    public static string Summary(string speakerName, string otherName, string claim) => $"{speakerName} and {otherName}: {claim}";

    static string? Swap(string claim, string name, string replacement)
    {
        var pattern = new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return pattern.IsMatch(claim) ? pattern.Replace(claim, _ => replacement) : null;
    }
}
