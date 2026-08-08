namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// One member that broke one law, as reported by whichever detector ran (the ArchUnitNET-based
/// scan or the hand-rolled assembly-reference scan) — the unit <see cref="ExemptionBaseline"/>
/// matches against and <see cref="DependencyLawAssert"/> reports on failure.
/// </summary>
/// <param name="LawId">One of <see cref="LawId"/>'s constants.</param>
/// <param name="Member">The offending type or assembly's full name, naming the offender at the
/// detector's granularity — assembly for the reference laws (L1, L4-references: an AssemblyRef
/// table and a deps.json libraries map are both assembly/package-scoped, with no type-level data
/// to report), type for the usage laws (L2: ArchUnitNET walks into method bodies and reports the
/// specific type that used the forbidden dependency). Must match the corresponding
/// <see cref="ArchitectureExemption.Member"/> string exactly for an exemption to apply.</param>
/// <param name="Detail">Human-readable detail for the failure message (what it depended on).</param>
internal sealed record LawViolation(string LawId, string Member, string Detail);
