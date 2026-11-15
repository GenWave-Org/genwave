using System.Text;

namespace GenWave.Core.Domain;

/// <summary>The rules every <c>life</c> store and the extractor share (SPEC F218.18).</summary>
public static class LifeMemoryRules
{
    /// <summary>Most extracted life rows one persona keeps; the oldest never-aired is evicted past this.</summary>
    public const int Cap = 100;

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
