using System.Text;

namespace GenWave.Core.Domain;

/// <summary>The rules every <c>life</c> store and the extractor share (SPEC F218.18).</summary>
public static class LifeMemoryRules
{
    /// <summary>Most extracted life rows one persona keeps; the oldest never-aired is evicted past this.</summary>
    public const int Cap = 100;

    /// <summary>Most new extracted life rows one persona gains per <see cref="NewWindow"/> (STORY-537, PLAN T704a).</summary>
    public const int MaxNewPerWindow = 3;

    /// <summary>The rolling window <see cref="MaxNewPerWindow"/> counts over; about one two-hour shift.</summary>
    public static readonly TimeSpan NewWindow = TimeSpan.FromHours(2);

    /// <summary>Most existing life rows shown to the extractor model so it asks only for what is new.</summary>
    public const int KnownShown = 30;

    /// <summary>Most forgotten life rows the extractor's code-side checks compare a new claim against.</summary>
    public const int ForgottenChecked = 50;

    /// <summary>
    /// The text two claims are compared on: lowercase, punctuation dropped, whitespace collapsed. Two claims
    /// with the same normalised text for one persona are the same claim.
    /// </summary>
    public static string Normalise(string claim)
    {
        var builder = new StringBuilder(claim.Length);
        var pendingSpace = false;
        foreach (var ch in claim)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }
}
