using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// One memory row in <see cref="PersonaMemoryController"/>'s responses (SPEC F218.24).
/// </summary>
/// <param name="Id">The row id the PATCH and DELETE routes name.</param>
/// <param name="Kind">lore, episode or life.</param>
/// <param name="Source">authored, accrued, extracted or drafted.</param>
/// <param name="Text">What the DJ may say.</param>
/// <param name="AiredCount">How many times it has aired.</param>
/// <param name="Pending">A drafted row awaiting approval; it is never recalled.</param>
/// <param name="Forgotten">The operator told the DJ to forget it.</param>
public sealed record PersonaMemoryRowDto(
    long Id,
    string Kind,
    string Source,
    string Text,
    int AiredCount,
    DateTime? LastAiredAt,
    DateTime CreatedAt,
    DateTime? ApprovedAt,
    DateTime? ForgottenAt,
    bool Pending,
    bool Forgotten)
{
    public static PersonaMemoryRowDto From(PersonaMemoryAdminRow row) => new(
        row.Id, row.Kind, row.Source, row.Content, row.AiredCount, row.LastAiredAt, row.CreatedAt,
        row.ApprovedAt, row.ForgottenAt, row.Pending, row.Forgotten);
}
