namespace GenWave.Core.Domain;

/// <summary>
/// One <c>station.persona_memory</c> row as the operator's memory view sees it (SPEC F218.24): the row plus
/// the lifecycle stamps the recall path keys on. <see cref="Source"/> is the stored text
/// (authored, accrued, extracted or drafted) because <see cref="PersonaMemorySource"/> only names the two the
/// older recording seam writes.
/// </summary>
public sealed record PersonaMemoryAdminRow(
    long Id,
    string Kind,
    string Source,
    string Content,
    int AiredCount,
    DateTime? LastAiredAt,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    DateTime? ForgottenAt)
{
    /// <summary>A drafted row nobody has approved yet; it is never recalled (SPEC F218.20).</summary>
    public bool Pending => Source == MemorySources.Drafted && ApprovedAt is null;

    /// <summary>The operator told the DJ to forget this row (SPEC F218.19).</summary>
    public bool Forgotten => ForgottenAt is not null;
}
