namespace GenWave.Core.Domain;

/// <summary>
/// One tagline of a persona and whether it may air now (SPEC F218.30–.31).
/// </summary>
/// <param name="Span">The tagline as written: a quoted span of a quirk, or the card's <c>tagline</c> field.</param>
/// <param name="Available">False when the hourly quota, the cooldown, or a spent once-a-shift cap holds it back.</param>
/// <param name="QuirkText">The quirk the span was quoted from, verbatim; <see langword="null"/> for the card's <c>tagline</c> field.</param>
public sealed record TaglineStatus(string Span, bool Available, string? QuirkText = null);
