namespace GenWave.Core.Domain;

/// <summary>Minimal projection of a library row (id + display name).</summary>
public sealed record LibraryInfo(long Id, string Name);
