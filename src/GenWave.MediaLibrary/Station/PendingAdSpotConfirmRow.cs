namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper's flat projection of <see cref="AdSpotRepository.ListPendingConfirmsAsync"/>'s own two-column
/// read (gh-#854) — <see cref="PendingAdSpotRetireRow"/>'s own shape, one marker over.
/// </summary>
sealed class PendingAdSpotConfirmRow
{
    public long Id { get; set; }
    public long PendingConfirmMediaId { get; set; }
}
