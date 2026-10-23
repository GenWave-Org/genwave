using System.Security.Cryptography;
using System.Text;
using GenWave.VoiceOff.Listening;

namespace GenWave.VoiceOff.Serving;

/// <summary>Puts a session's pairs in a judge's own order. The seed is a stable hash of the judge and the session, so a reload keeps the order and two judges differ.</summary>
internal static class SessionShuffle
{
    internal static IReadOnlyList<SessionPair> Order(IReadOnlyList<SessionPair> pairs, string judge, string sessionId)
    {
        Random rng = new(Seed(judge, sessionId));
        SessionPair[] ordered = [.. pairs];
        for (int i = ordered.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }

        return ordered;
    }

    // Not string.GetHashCode: that is randomised per process.
    internal static int Seed(string judge, string sessionId) =>
        BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(judge + "\n" + sessionId)), 0);
}
