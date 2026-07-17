namespace GenWave.Core.Domain;

/// <summary>
/// Admin projection of a library row: id, display name, and the count of associated media rows.
/// Returned by <see cref="GenWave.Core.Abstractions.ILibraryRepository.GetAllWithMediaCountAsync"/>
/// and used by the library CRUD controller (STORY-047).
/// </summary>
public sealed record LibraryAdminInfo(long Id, string Name, int MediaCount);
