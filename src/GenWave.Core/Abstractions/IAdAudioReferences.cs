namespace GenWave.Core.Abstractions;

/// <summary>
/// Station-side half of the retired ad audio sweep (gh-#3): which ad audio <c>station.ad_spot</c> still
/// points at. The two schemas share no foreign key (the db/22 boundary), so the sweep asks this seam
/// before it deletes anything. Its own seam, kept off <c>IAdSpotStore</c> and its many test doubles.
/// </summary>
public interface IAdAudioReferences
{
    /// <summary>Every media id a spot references — its audio, its bed, and any pending swap — except the
    /// audio of a spot retired before <paramref name="retiredBefore"/>, which is free to go.</summary>
    Task<IReadOnlySet<long>> ListMediaIdsInUseAsync(DateTimeOffset retiredBefore, CancellationToken ct);

    /// <summary>Every preview path a spot currently has stamped.</summary>
    Task<IReadOnlySet<string>> ListPreviewPathsAsync(CancellationToken ct);
}
