namespace GenWave.Tts;

/// <summary>
/// One line of a <see cref="CastAssemblyRequest"/> (SPEC F161.2, STORY-391, PLAN T401) — the widened
/// sibling of <see cref="GenWave.Core.Domain.CrosstalkAiredLine"/>'s own two-fixed-role shape: a
/// free-form <see cref="Tag"/> instead of a <see cref="GenWave.Core.Domain.CrosstalkSpeaker"/> enum,
/// since a voice cast carries 1-3 OPERATOR-NAMED voices (an ad script's own <c>TAG: line</c> wire
/// format, <c>GenWave.Ads.AdScriptLine</c>'s shape one project over — this project never references
/// that one, L10), never just Host/Neighbor. No <c>IsInterjection</c> flag either: a cast render
/// never overlaps two voices (SPEC F161.2 carries no interjection concept for ads) — every transition
/// uses the same jittered "ordinary line" gap <see cref="CrosstalkTimeline"/> already plans for
/// crosstalk's own non-interjecting lines (see <see cref="CrosstalkAssembler.AssembleCastAsync"/>'s
/// own remarks).
/// </summary>
/// <param name="Tag">Which <see cref="CastAssemblyRequest.Cast"/> member speaks this line.</param>
/// <param name="Text">The line's spoken text.</param>
public sealed record CastLine(string Tag, string Text);
