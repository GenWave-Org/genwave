namespace GenWave.Core.Events;

/// <summary>
/// An admin write touched a library row. <paramref name="Change"/> names the mutation
/// ("created", "renamed", "deleted").
/// </summary>
public sealed record LibraryMutated(string Change, long LibraryId) : StationEvent;
