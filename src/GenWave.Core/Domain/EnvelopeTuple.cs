namespace GenWave.Core.Domain;

/// <summary>
/// One DISTINCT effective envelope <see cref="Abstractions.IRotFindingStore.ReconcileUnreachableAsync"/>
/// reconciles against (SPEC F153.8; STORY-378; PLAN T376, gh-#529) — the <c>Garden.UnreachableGardenerPass</c>
/// caller's own port input, built from the weekly grid's per-field-fallback effective envelope
/// (mirroring <c>GenWave.Orchestration.ScheduleResolver.BuildSegmentEnvelope</c>'s own formula
/// exactly, T376 ORCHESTRATOR ruling), never re-derived inside <c>Garden.RotFindingRepository</c>
/// itself.
///
/// <para>
/// <see cref="GenresLower"/> is the pass's OWN already-normalized list — lower-cased, trimmed,
/// blanks dropped, de-duplicated, and sorted — so two segments naming the same genres in a different
/// order or casing fold to the textually IDENTICAL tuple ("distinctness is textual", T376
/// ORCHESTRATOR ruling); empty means no genre constraint (every genre admitted), the same
/// <c>SegmentEnvelope.Genres</c> convention this type's own source carries forward. This record
/// never re-normalizes it — a caller handing in un-normalized casing gets exactly that casing
/// compared, byte for byte, against whatever <c>RotFindingRepository</c>'s own SQL does with it.
/// </para>
///
/// <para>
/// <b>Equality is overridden, not the compiler-synthesized record default</b> — <see cref="GenresLower"/>
/// is an <see cref="IReadOnlyList{T}"/>, and the record-generated <c>Equals</c> would compare that
/// property by reference (two lists with identical contents but different instances would compare
/// UNEQUAL), which would silently break <c>UnreachableGardenerPass</c>'s own <c>.Distinct()</c> dedup
/// pass. <see cref="Equals(EnvelopeTuple?)"/>/<see cref="GetHashCode"/> below compare/hash
/// <see cref="GenresLower"/> element-by-element instead (<c>SequenceEqual</c> is therefore
/// ORDER-SENSITIVE — the caller's own sort is what makes two differently-ordered inputs compare
/// equal, not this type).
/// </para>
/// </summary>
/// <param name="GenresLower">The tuple's own genre allow-list, already lower-cased/trimmed/deduped/
/// sorted by the caller; empty admits every genre.</param>
/// <param name="EnergyMin">The tuple's own energy band lower bound, in <c>[0, 1]</c>.</param>
/// <param name="EnergyMax">The tuple's own energy band upper bound, in <c>[EnergyMin, 1]</c>.</param>
public sealed record EnvelopeTuple(IReadOnlyList<string> GenresLower, double EnergyMin, double EnergyMax)
{
    public bool Equals(EnvelopeTuple? other) =>
        other is not null
        && GenresLower.SequenceEqual(other.GenresLower, StringComparer.Ordinal)
        && EnergyMin.Equals(other.EnergyMin)
        && EnergyMax.Equals(other.EnergyMax);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var genre in GenresLower)
            hash.Add(genre, StringComparer.Ordinal);
        hash.Add(EnergyMin);
        hash.Add(EnergyMax);
        return hash.ToHashCode();
    }
}
