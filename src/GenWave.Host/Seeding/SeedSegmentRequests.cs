using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Seeding;

/// <summary>
/// The one place the boot seed's <see cref="SafeSegmentRequest"/> is built, so a re-render (SPEC F219.22) says exactly
/// what the first render did. <c>{StationName}</c> expansion stays <see cref="SafeSegmentAuthor"/>'s job alone.
/// </summary>
static class SeedSegmentRequests
{
    public static SafeSegmentRequest Build(StationOptions station, string defaultVoice, long libraryId) => new(
        Text: station.Safe.SeedMessage,
        LibraryId: libraryId,
        StationName: station.Name,
        DefaultVoice: defaultVoice,
        AuthoredRoot: station.Safe.AuthoredRoot,
        BedDuckDb: station.Safe.BedDuckDb,
        BedPadSeconds: station.Safe.BedPadSeconds,
        Title: SafeLoopSeeder.SeedTitle);
}
