namespace GenWave.Host.Seeding;

/// <summary>
/// Which media rows are <see cref="SafeLoopSeeder"/> content and the voice each was made in (SPEC F219.22/F219.24).
/// Only rows listed here are ever re-rendered, so audio Dean supplied is never touched by construction. Kept in an
/// internal <c>station.settings</c> key, never on the settings API.
/// </summary>
public interface ISafeContentLedger
{
    Task<IReadOnlyList<SafeContentRecord>> ListAsync(CancellationToken ct);

    Task SaveAsync(IReadOnlyList<SafeContentRecord> records, CancellationToken ct);
}
