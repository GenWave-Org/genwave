using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests.Fakes;

/// <summary>In-memory media rows read through <see cref="IAdminMediaLookup"/> and switched on or off through <see cref="IAuthoredCatalogWriter"/>.</summary>
sealed class FakeSweepCatalog : IAdminMediaLookup, IAuthoredCatalogWriter
{
    readonly Dictionary<long, (AdminMediaDto Row, long LibraryId)> rows = [];

    /// <summary>Makes the next switch-off throw, once.</summary>
    public bool FailNextSwitchOff { get; set; }

    public void AddRow(long id, long libraryId, string locator, bool eligible) =>
        rows[id] = (new AdminMediaDto(
            MediaId: id.ToString(), Locator: locator, Format: "wav", State: "ready", DurationMs: 5000,
            Title: "Please Stand By (Station Default)", Artist: "Test Station", Album: null, Genre: null, Year: null,
            IntegratedLufs: -16.0, TruePeakDbtp: -1.0, Measurable: true, CueInSec: null, CueOutSec: null,
            Eligible: eligible, Version: "1"), libraryId);


    public bool Exists(long id) => rows.ContainsKey(id);

    public bool IsEligible(long id) => rows[id].Row.Eligible;

    public Task<(AdminMediaDto Row, long LibraryId)?> GetByIdWithLibraryAsync(long id, CancellationToken ct) =>
        Task.FromResult<(AdminMediaDto Row, long LibraryId)?>(rows.TryGetValue(id, out var found) ? found : null);

    public Task<bool> SetEligibleAsync(long mediaId, bool eligible, CancellationToken ct)
    {
        if (FailNextSwitchOff)
        {
            FailNextSwitchOff = false;
            throw new InvalidOperationException("catalog write failed");
        }

        if (!rows.TryGetValue(mediaId, out var found))
            return Task.FromResult(false);
        rows[mediaId] = (found.Row with { Eligible = eligible }, found.LibraryId);
        return Task.FromResult(true);
    }

    public Task<long> InsertAuthoredAsync(AuthoredMediaInsert insert, CancellationToken ct) =>
        throw new NotSupportedException("the sweep inserts through ISafeSegmentAuthor, never directly");
}
