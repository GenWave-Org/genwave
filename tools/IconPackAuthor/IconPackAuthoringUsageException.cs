namespace GenWave.IconPackAuthor;

/// <summary>
/// Thrown for a malformed invocation — a missing/unrecognized CLI flag, an unreadable mapping file, a
/// duplicate mapping target name. Distinct from <see cref="SvgConversionException"/> (a per-glyph SVG
/// content failure): this is "the run itself cannot start," caught once at the top of
/// <see cref="Program"/> and reported with <see cref="IconPackAuthoringOptions.UsageText"/>, never
/// treated as one of the glyph failures STORY-338 AC1 names.
/// </summary>
public sealed class IconPackAuthoringUsageException(string message) : Exception(message);
