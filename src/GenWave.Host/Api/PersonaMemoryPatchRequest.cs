namespace GenWave.Host.Api;

/// <summary>
/// Body of <c>PATCH /api/personas/{id}/memory/{rowId}</c>; send at least one field.
/// <paramref name="Approved"/> only ever means <c>true</c> (approve a drafted row). <paramref name="Forgotten"/>
/// true forgets the row, false un-forgets it.
/// </summary>
public sealed record PersonaMemoryPatchRequest(string? Text, bool? Approved, bool? Forgotten);
