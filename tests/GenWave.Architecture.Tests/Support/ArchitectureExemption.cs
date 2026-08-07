namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// F105.2's exemption shape: a named, dated, reasoned escape hatch for exactly one offending
/// member/type under exactly one law. There is no namespace- or assembly-wide "blanket exempt" —
/// matching this one member's full name, for this one law, is the only way a violation is
/// silenced, and every entry that does it is listed in <see cref="ExemptionBaseline"/> where a
/// reviewer can see it. A violation whose full name is not on the list fails, no matter how small.
/// </summary>
/// <param name="LawId">One of <see cref="LawId"/>'s constants.</param>
/// <param name="Member">The offending type's full name, exactly as the detector reports it (e.g.
/// <see cref="ArchUnitNET.Fluent.EvaluationResult.EvaluatedObjectIdentifier"/>'s string form, or
/// the assembly simple name for the assembly-reference laws).</param>
/// <param name="Date">ISO date the exemption was recorded (adoption date for pre-existing debt).</param>
/// <param name="Reason">Why this one member is allowed to violate the law — either "this is the
/// law's own designed exemption" or "pre-existing debt, not trivial to fix in the adopting diff".</param>
internal sealed record ArchitectureExemption(string LawId, string Member, string Date, string Reason);
