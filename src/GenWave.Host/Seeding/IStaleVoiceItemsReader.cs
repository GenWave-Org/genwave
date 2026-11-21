namespace GenWave.Host.Seeding;

/// <summary>How many items (ads and boot-seed content) are still in the old voice (SPEC F219.23).</summary>
public interface IStaleVoiceItemsReader
{
    /// <summary>The count, or null when it could not be worked out. Null is "unknown", never zero: zero means everything is current.</summary>
    Task<int?> CountAsync(CancellationToken ct);
}
