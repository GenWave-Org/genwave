using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Host.Options;

namespace GenWave.Host.Seeding;

/// <summary>
/// The seed row is the one in the safe library titled <see cref="SafeLoopSeeder.SeedTitle"/> whose file sits under
/// <c>Station:Safe:AuthoredRoot</c>, and that can still air. Both placement and title must hold: an uploaded file (SPEC F219.24) is never under the authored
/// root, so it cannot be adopted even if Dean titled it the same.
/// </summary>
public sealed class SeedRowFinder(
    ILibraryRepository libraries,
    IAdminMediaQuery mediaQuery,
    IOptionsMonitor<StationOptions> stationMonitor) : ISeedRowFinder
{
    public async Task<long?> FindSeedMediaIdAsync(CancellationToken ct)
    {
        var library = await libraries.GetByNameAsync(SafeLoopSeeder.SafeLibraryName, ct);
        if (library is null)
            return null;

        var root = Path.GetFullPath(stationMonitor.CurrentValue.Safe.AuthoredRoot) + Path.DirectorySeparatorChar;
        var page = await mediaQuery.ListAdminAsync(
            new GenWave.Core.Domain.LibraryScope([library.Id]),
            new MediaQuery(LibraryId: library.Id, Q: SafeLoopSeeder.SeedTitle, Limit: 50), ct);

        // Only a row that can air is the seed worth replacing; several matches (an old take Dean switched off beside
        // the current one) resolve to the eligible one.
        var seed = page.Items.FirstOrDefault(row =>
            string.Equals(row.Title, SafeLoopSeeder.SeedTitle, StringComparison.Ordinal)
            && row.Eligible && !row.NeverPlay
            && Path.GetFullPath(row.Locator).StartsWith(root, StringComparison.Ordinal));

        return seed is not null && long.TryParse(seed.MediaId, out var id) ? id : null;
    }
}
