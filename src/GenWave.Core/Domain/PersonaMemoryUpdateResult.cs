namespace GenWave.Core.Domain;

/// <summary>The outcome of an edit; <paramref name="Row"/> is the stored row, set only when <see cref="PersonaMemoryUpdateStatus.Updated"/>.</summary>
public sealed record PersonaMemoryUpdateResult(PersonaMemoryUpdateStatus Status, PersonaMemoryAdminRow? Row);
