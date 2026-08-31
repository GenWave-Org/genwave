namespace GenWave.Host;

/// <summary>
/// Which of <see cref="RotationPredicateRules.Validate"/>'s three checks failed, if any — the field a
/// caller should name in its own refusal.
/// </summary>
internal enum RotationPredicateField
{
    /// <summary>The pair validates: at least one bound set, <c>maxPlays</c> ≥ 0 (if set),
    /// <c>notAiredWithinDays</c> in <see cref="RotationPredicateRules.MinNotAiredWithinDays"/>–
    /// <see cref="RotationPredicateRules.MaxNotAiredWithinDays"/> (if set).</summary>
    None,

    /// <summary>Neither <c>maxPlays</c> nor <c>notAiredWithinDays</c> is set — SPEC F152.1's own
    /// "at least one" rule.</summary>
    Rotation,

    /// <summary><c>maxPlays</c> is set and negative.</summary>
    MaxPlays,

    /// <summary><c>notAiredWithinDays</c> is set and outside
    /// <see cref="RotationPredicateRules.MinNotAiredWithinDays"/>–<see cref="RotationPredicateRules.MaxNotAiredWithinDays"/>.</summary>
    NotAiredWithinDays,
}

/// <summary>
/// The one shared SPEC F152.1/F152.5 rotation-predicate validation (PLAN T363 review MED-3) — the
/// three rules and their literal bounds, previously hand-duplicated between
/// <see cref="Api.ShowRotationController"/>'s own <c>ParseRotationBody</c> (the PUT <c>/api/shows/{id}</c>
/// edge) and <see cref="Shows.ShowManifestParser"/>'s own <c>ParseEnvelope</c> (the
/// <c>POST /api/shows/{slug}/import</c> edge, SPEC F152.6). Mirrors <see cref="SchemaVersionProbe"/>'s
/// own "one shared Host-root type, format/route-specific callers keep their own wrapping" shape: this
/// returns WHICH rule failed (<see cref="RotationPredicateField"/>), never a rendered message — the two
/// callers' own refusal TEXT differs (one names a bare field with a trailing period, the other a
/// <c>"show manifest '…' envelope.rotation.field"</c> path with none) and stays exactly as it already
/// was; only the RULE and the BOUND LITERALS live here now.
/// </summary>
internal static class RotationPredicateRules
{
    /// <summary>SPEC F152.1/F152.5's own inclusive lower bound for <c>notAiredWithinDays</c>.</summary>
    public const int MinNotAiredWithinDays = 1;

    /// <summary>SPEC F152.1/F152.5's own inclusive upper bound for <c>notAiredWithinDays</c>.</summary>
    public const int MaxNotAiredWithinDays = 3650;

    /// <summary>Validates an already-shape-checked <paramref name="maxPlays"/>/
    /// <paramref name="notAiredWithinDays"/> pair (each caller's own JSON-shape gate — "must be a whole
    /// number" — runs before this and is NOT this method's concern) against the three SPEC F152.1/
    /// F152.5 rules, in the same order both callers already checked them: at least one bound set,
    /// then <c>maxPlays</c> ≥ 0, then <c>notAiredWithinDays</c> in range.</summary>
    public static RotationPredicateField Validate(int? maxPlays, int? notAiredWithinDays)
    {
        if (maxPlays is null && notAiredWithinDays is null)
            return RotationPredicateField.Rotation;

        if (maxPlays is < 0)
            return RotationPredicateField.MaxPlays;

        if (notAiredWithinDays is not null
            && (notAiredWithinDays < MinNotAiredWithinDays || notAiredWithinDays > MaxNotAiredWithinDays))
            return RotationPredicateField.NotAiredWithinDays;

        return RotationPredicateField.None;
    }
}
