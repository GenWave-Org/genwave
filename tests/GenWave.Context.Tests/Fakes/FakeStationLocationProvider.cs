namespace GenWave.Context.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Mutable <see cref="IStationLocationProvider"/> double: <see cref="Current"/> is whatever
/// <see cref="Location"/> is currently set to, read fresh on every access (mirrors this seam's own
/// "never cache" contract) — a fact can flip it mid-test to prove a provider re-reads it.
/// </summary>
sealed class FakeStationLocationProvider : IStationLocationProvider
{
    public StationLocation Location { get; set; } = new(string.Empty, string.Empty, string.Empty);

    public StationLocation Current => Location;
}
