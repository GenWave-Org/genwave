namespace GenWave.Core.Domain;

/// <summary>
/// SPEC F115.1's field-length budgets for an authored/edited <see cref="Show"/> — the app seam's 1×
/// hard line (name ≤60, tagline ≤120, flavor ≤400 chars; reasoned-not-fitted, the F89.5 posture).
/// <see cref="Abstractions.IShowStore.CreateAsync"/>/<see cref="Abstractions.IShowStore.UpdateAsync"/>
/// enforce these before the write ever reaches Postgres. The manifest parser's own 2× headroom (PLAN
/// T254) and catalog lint's WARN-over-1× posture are separate, later concerns — not this seam's.
/// </summary>
public static class ShowBudgets
{
    public const int NameMaxChars = 60;
    public const int TaglineMaxChars = 120;
    public const int FlavorMaxChars = 400;

    /// <summary>
    /// The first budget <paramref name="draft"/> violates, checked in field order (name, tagline,
    /// flavor) so a draft violating more than one field always reports the same one first — the rule
    /// lives beside its own constants (PLAN T239 review) so later writers (T240/T244/T254) share the
    /// identical check order instead of each re-deriving it. <c>null</c> when every field is within
    /// budget.
    /// </summary>
    public static ShowBudgetField? FirstViolation(ShowDraft draft)
    {
        if (draft.Name.Length > NameMaxChars) return ShowBudgetField.Name;
        if (draft.Tagline is { Length: > TaglineMaxChars }) return ShowBudgetField.Tagline;
        if (draft.Flavor is { Length: > FlavorMaxChars }) return ShowBudgetField.Flavor;
        return null;
    }
}
