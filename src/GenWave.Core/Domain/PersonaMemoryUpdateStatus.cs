namespace GenWave.Core.Domain;

/// <summary>How an operator edit of a memory row ended.</summary>
public enum PersonaMemoryUpdateStatus
{
    /// <summary>The whole change was applied.</summary>
    Updated,

    /// <summary>No such row belongs to that persona; nothing changed.</summary>
    NotFound,

    /// <summary>Approve was asked of a row that is not a drafted one; nothing changed.</summary>
    NotDrafted,
}
