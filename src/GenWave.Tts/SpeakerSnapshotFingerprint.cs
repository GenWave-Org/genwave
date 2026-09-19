namespace GenWave.Tts;

using System.Globalization;

/// <summary>
/// Builds <see cref="GenWave.Core.Domain.SpeakerSnapshot.ContentHash"/>'s input string — the SAME
/// terms <see cref="TtsSegmentSource"/>'s own <c>ComputeHash</c> folds into its cache key today,
/// minus <c>text</c> and <c>stationId</c>. <c>ComputeHash</c> puts <c>text</c> at position 1 and
/// <c>stationId</c> at position 3, so T526 must either splice those two back into positions 1 and 3
/// or refactor <c>TtsSegmentSource.ComputeHash</c> to accept this string — an appended tuple would
/// NOT reproduce the ambient digest. A plain "|"-joined string, not itself hashed: T526 is the one
/// place that ever turns the combined terms into the actual SHA256 digest TtsSegmentSource's
/// file-exists cache check compares — hashing here too would just be thrown away the moment T526
/// re-hashes the concatenation, and would let this string's own term ORDER drift from
/// TtsSegmentSource.ComputeHash's without either side noticing.
/// </summary>
static class SpeakerSnapshotFingerprint
{
    /// <summary>
    /// Term order mirrors <c>TtsSegmentSource.ComputeHash</c> exactly (its own <c>text</c> and
    /// <c>stationId</c> terms simply omitted): voice, station corrections, card corrections,
    /// station pronunciations, card pronunciations, merge-policy version, pace.
    /// </summary>
    public static string Compute(
        string voice,
        string stationCorrectionsContentHash,
        string cardCorrectionsContentHash,
        string stationPronunciationsContentHash,
        string cardPronunciationsContentHash,
        string mergePolicyVersion,
        double pace) =>
        voice + "|" + stationCorrectionsContentHash + "|" + cardCorrectionsContentHash +
        "|" + stationPronunciationsContentHash + "|" + cardPronunciationsContentHash +
        "|" + mergePolicyVersion + "|" + pace.ToString("0.####", CultureInfo.InvariantCulture);
}
