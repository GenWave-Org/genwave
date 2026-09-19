namespace GenWave.Tts;

using System.Globalization;

/// <summary>
/// Builds <see cref="GenWave.Core.Domain.SpeakerSnapshot.ContentHash"/>'s input string — the SAME
/// terms <see cref="TtsSegmentSource"/>'s own <c>ComputeHash</c> folds into its cache key today,
/// minus <c>text</c> and <c>stationId</c>.
///
/// <para>
/// <b>T526 deliberately did NOT reproduce the ambient digest.</b> An earlier revision of this
/// comment read as an instruction to splice <c>text</c> and <c>stationId</c> back into positions 1
/// and 3 so both paths would agree on a key; the shipped code does the opposite.
/// <c>TtsRenderKey.ComputeSnapshotHash</c> hashes <c>text|voice|stationId|</c> plus this whole
/// string as one fourth term, so the snapshot arm and the ambient arm occupy DISJOINT key spaces:
/// for a single <c>(text, voice, stationId)</c> the ambient input carries five separators after
/// <c>stationId</c> and the snapshot input six, and no term in the REMAINDER after
/// <c>stationId</c> can contain a <c>"|"</c> — those are hash hex, the merge-policy version and a
/// <c>0.####</c> pace, never authored copy — so the two can never collide. A snapshot-driven render therefore never reuses a clip
/// the ambient path wrote, and vice versa — <c>TtsRenderKey</c>'s own remarks carry what that
/// costs.
/// </para>
///
/// <para>
/// A plain "|"-joined string, not itself hashed: <c>TtsRenderKey</c> is the one place that turns
/// the combined terms into the actual SHA256 digest the file-exists cache check compares — hashing
/// here too would just be thrown away when that concatenation is re-hashed, and would let this
/// string's own term ORDER drift from <c>TtsSegmentSource.ComputeHash</c>'s without either side
/// noticing.
/// </para>
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
