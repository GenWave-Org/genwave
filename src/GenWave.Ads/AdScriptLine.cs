namespace GenWave.Ads;

/// <summary>
/// One parsed <c>TAG: line</c> from an ad script (SPEC F160.3, STORY-390 AC1) — <see cref="Tag"/> is
/// already validated uppercase-alphanumeric (<see cref="AdScriptParser"/>'s own tag pattern), <see
/// cref="Text"/> is the trimmed spoken text after the colon, within the caller's per-line char ceiling.
/// </summary>
/// <param name="Tag">The voice tag this line is spoken by (e.g. <see cref="AdScriptParser.AnnouncerTag"/>).</param>
/// <param name="Text">The spoken text, trimmed, never empty.</param>
public sealed record AdScriptLine(string Tag, string Text);
