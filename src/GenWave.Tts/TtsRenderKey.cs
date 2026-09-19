namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The snapshot-driven half of <see cref="TtsSegmentSource"/>'s cache-key hash (SPEC F189.3,
/// STORY-456, PLAN T526) — computed over the caller's own <c>text</c>/<c>voice</c>/<c>stationId</c>
/// plus the resolved <see cref="GenWave.Core.Domain.SpeakerSnapshot.ContentHash"/>, rather than
/// <see cref="TtsSegmentSource"/>'s own five-fingerprint ambient formula
/// (<see cref="TtsSegmentSource"/>'s private <c>ComputeHash</c>). A snapshot already folds every
/// ambient term (station+card corrections/pronunciations, the merge-policy version, pace) into its
/// own <c>ContentHash</c> at resolve time — see <c>SpeakerSnapshotFingerprint</c> — so re-hashing
/// those same terms a second time here would be redundant, not more correct.
///
/// <para>
/// <b>Cost of the disjoint key space:</b> this formula and <see cref="TtsSegmentSource"/>'s ambient
/// <c>ComputeHash</c> can never produce the same digest for the same <c>(text, voice, stationId)</c>
/// (see <c>SpeakerSnapshotFingerprint</c>'s remarks for why the two inputs can never collide) —
/// so the snapshot arm can never hit a cache entry the ambient arm wrote, and vice versa. The FIRST
/// snapshot-driven render of an otherwise-evergreen <c>StationId</c>/<c>LeadIn</c>/<c>BackAnnounce</c>
/// clip therefore always re-synthesizes even when the ambient path already cached that exact text —
/// and the ambient file it leaves behind is now orphaned on the named volume (nothing ever re-reads
/// it once the segment is snapshot-driven going forward). This is an accepted one-time cost of
/// keeping the two arms' cache keys independently correct, not a bug to fix here.
/// </para>
///
/// Kept out of <see cref="TtsSegmentSource"/> itself so that already-large file gains only the
/// two-arm branch (SPEC F189.3's null/non-null split), not a second hash formula inline.
/// </summary>
static class TtsRenderKey
{
    /// <summary>
    /// Term order: text, voice, stationId, the snapshot's own content hash — the same first three
    /// terms <see cref="TtsSegmentSource"/>'s ambient <c>ComputeHash</c> uses, with every ambient
    /// fingerprint collapsed into the one term the snapshot already carries.
    /// </summary>
    public static string ComputeSnapshotHash(string text, string voice, string stationId, string speakerContentHash) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(text + "|" + voice + "|" + stationId + "|" + speakerContentHash)));
}
