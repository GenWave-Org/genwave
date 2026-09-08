namespace GenWave.Ads;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Deterministic RNG seeding from a small set of canonical terms (SPEC F167.2; STORY-402; PLAN T415)
/// — the <c>CrosstalkTimeline.ComputeSeed</c> shape (GenWave.Tts) reused here rather than widened:
/// that method is <c>internal</c> to its own project, and this project must never grow an
/// <c>InternalsVisibleTo</c> just to reach three lines of hashing. <see cref="FromTerms"/> hashes
/// <see cref="Canonical"/>'s own newline-joined string with SHA256 and takes the first 4 bytes as a
/// signed <c>int</c> seed; <see cref="Random.Shared"/> is never used anywhere a caller needs the SAME
/// pick on a later call (STORY-402 AC4's own "regeneration re-casts identically" contract —
/// <see cref="AdCastPicker"/> is this task's caller; a later render-scheduling task can reuse this same
/// helper with its own single-term seed).
/// </summary>
internal static class AdDeterministicSeed
{
    /// <summary>Newline-joins <paramref name="terms"/> into the exact string <see cref="FromTerms"/>
    /// hashes — exposed separately so a spec can pin the literal string a seed is computed from,
    /// independent of the hash itself (e.g. <c>"42\nowner\nAcme"</c>).</summary>
    public static string Canonical(params string[] terms) => string.Join('\n', terms);

    public static int FromTerms(params string[] terms)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(terms)));
        return BitConverter.ToInt32(hash, 0);
    }
}
