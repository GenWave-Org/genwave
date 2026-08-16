namespace GenWave.IconPackAuthor;

/// <summary>
/// Thrown by any converter step (<see cref="SvgGlyphConverter"/>, <see cref="PathDataTransform"/>,
/// <see cref="PointsTransform"/>) the moment ONE glyph's source SVG carries a construct this app's
/// closed schema (SPEC F130.1) cannot express. Caught exactly once, at the top of
/// <see cref="SvgGlyphConverter.Convert"/> — this is the "fail loudly, name the offending glyph and
/// construct" mechanism PLAN T305 calls for; nothing catches this deeper and silently drops the
/// offending element instead.
/// </summary>
public sealed class SvgConversionException(string message) : Exception(message);
