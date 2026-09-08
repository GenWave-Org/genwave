using System.Diagnostics;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// SPEC F165.3's role-based cue policy — decides what <see cref="JinglePackController.Install"/>
/// actually stores on <c>library.media.cue_in_sec</c>/<c>cue_out_sec</c> for one staged asset, once
/// <see cref="Abstractions.ICueAnalyzer"/> (the SAME silence/energy-threshold analyzer every other
/// scanned row already runs through) has produced its own <see cref="CuePoints"/> for that file.
///
/// <para>
/// A <c>bed</c> (background music, played in full under generated ad copy — gh-#707: never call it a
/// "bed" in any user-facing string, only the closed-set database token stays that word) and a
/// <c>sting</c> (a short stand-alone stab used whole, never independently trimmed) both get
/// FULL-LENGTH cues: <paramref name="analyzed"/>'s own silence-trimmed points are discarded in favour
/// of the file's own true start and end. Neither role is ever played as a stand-alone segment the way
/// an ordinary scanned clip is — a bed plays under something else for its whole duration, a sting
/// plays in full as a single bump — so trimming either to its own detected "musical" start/end would
/// only ever clip audio a caller actually wanted.
/// </para>
///
/// <para>
/// A <c>station_id</c> (aired stand-alone, the same way a liner or promo already is) keeps
/// <paramref name="analyzed"/>'s own cue points unmodified — this is the ONE jingle role scanned like
/// every other library row, since it is played the same way one is.
/// </para>
///
/// <para>
/// A null <paramref name="analyzed"/> means exactly what <see cref="Abstractions.ICueAnalyzer"/>'s own
/// contract already says it means everywhere else it is called — no silence was found anywhere in the
/// file, so full-file playback is intended — and is NEVER a refusal here: a tight-cut sting or a
/// station ID that starts and ends hot is the normal shape of real jingle content, so a null analysis
/// falls back to the file's own full span, the same span <c>bed</c>/<c>sting</c> already get
/// unconditionally.
/// </para>
/// </summary>
internal static class JingleCuePolicy
{
    public static CuePoints Apply(string role, CuePoints? analyzed, double durationSec) => role switch
    {
        "bed" or "sting" => new CuePoints(0, durationSec),
        "station_id" => analyzed ?? new CuePoints(0, durationSec),
        _ => throw new UnreachableException(
            $"Unhandled jingle role \"{role}\" — CatalogJinglePackManifestSerializer's own closed-set gate should have refused this manifest before this method was ever called."),
    };
}
