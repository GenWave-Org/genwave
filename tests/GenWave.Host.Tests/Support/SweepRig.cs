namespace GenWave.Host.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Host.Seeding;
using GenWave.Host.Tests.Fakes;
using GenWave.TestSupport.Fakes;
using GenWave.Tts;

/// <summary>
/// A <see cref="SafeContentResweeper"/> over fakes at the I/O edges, on a station that has just switched from
/// kokoro to chatterbox (SPEC F219.22). Every item the ledger holds was made on kokoro.
/// </summary>
sealed class SweepRig
{
    public const long SafeLibraryId = 7;
    public const long FirstOldMediaId = 500;
    public const long FirstNewMediaId = FakeSweepAuthor.FirstNewMediaId;
    public const string Voice = "af_heart";
    public const string AuthoredRoot = "/authored";

    SweepRig(IReadOnlyList<SafeContentRecord> ledgerRecords, ISeedRowFinder? finder, bool gated)
    {
        Station = new StationOptions
        {
            Id = "test", Name = "Test Station", Voice = Voice,
            Safe = new StationSafeOptions { SeedMessage = "You're listening to {StationName}.", AuthoredRoot = AuthoredRoot },
        };
        Catalog = new FakeSweepCatalog();
        Author = new FakeSweepAuthor(Catalog, gated);
        Signal = new FakeOnAirSignal();
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        Ledger = new FakeSafeContentLedger();
        Ledger.SaveAsync(ledgerRecords, CancellationToken.None).GetAwaiter().GetResult();

        Resweeper = new SafeContentResweeper(
            Ledger, finder ?? new FakeSeedRowFinder(), Author, Catalog, Catalog, KokoroVoiceResolver.Instance,
            TestVoiceEngines.Active("chatterbox"), RenderBudget, new FakeOptionsMonitor<StationOptions>(Station), Signal, Time,
            NullLogger<SafeContentResweeper>.Instance);
    }

    public FakeRenderBudgetProvider RenderBudget { get; } = new(TimeSpan.FromSeconds(120));
    public StationOptions Station { get; }
    public FakeSweepCatalog Catalog { get; }
    public FakeSweepAuthor Author { get; }
    public FakeOnAirSignal Signal { get; }
    public FakeTimeProvider Time { get; }
    public FakeSafeContentLedger Ledger { get; }
    public SafeContentResweeper Resweeper { get; }

    public string CurrentMarker => SafeContentMarker.For("chatterbox", Voice);

    static string KokoroMarker => SafeContentMarker.For("kokoro", Voice);

    /// <summary>
    /// Ledger items made on kokoro, ids from <see cref="FirstOldMediaId"/>; with zero, one item already on
    /// chatterbox instead, so "nothing is stale" has something to leave alone.
    /// </summary>
    public static SweepRig AfterSwitch(int staleItems, bool gated = false)
    {
        var records = staleItems == 0
            ? [new SafeContentRecord(FirstOldMediaId, SafeContentMarker.For("chatterbox", Voice))]
            : Enumerable.Range(0, staleItems)
                .Select(i => new SafeContentRecord(FirstOldMediaId + i, KokoroMarker))
                .ToList();

        var rig = new SweepRig(records, finder: null, gated);
        foreach (var record in records)
            rig.Catalog.AddRow(record.MediaId, SafeLibraryId, $"{AuthoredRoot}/{record.MediaId}.wav", eligible: true);
        return rig;
    }

    /// <summary>
    /// A swap an earlier tick began whose ledger save committed and then threw: the ledger lists the replacement with a
    /// pending retire of the old row, but the replacement was switched off. The old row is still the live take.
    /// </summary>
    public static SweepRig WithASwapWhoseReplacementIsOff()
    {
        var rig = new SweepRig([new SafeContentRecord(FirstNewMediaId, SafeContentMarker.For("chatterbox", Voice), FirstOldMediaId)], finder: null, gated: false);
        rig.Catalog.AddRow(FirstOldMediaId, SafeLibraryId, $"{AuthoredRoot}/{FirstOldMediaId}.wav", eligible: true);
        rig.Catalog.AddRow(FirstNewMediaId, SafeLibraryId, $"{AuthoredRoot}/{FirstNewMediaId}.wav", eligible: false);
        return rig;
    }

    /// <summary>An empty ledger, and in the safe library only an upload Dean titled like the seed, stored outside the authored root.</summary>
    public static SweepRig WithUploadedImagingOnly()
    {
        var rig = new SweepRig([], FinderOver(UploadedRow), gated: false);
        rig.Catalog.AddRow(900, SafeLibraryId, UploadedRow.Locator, eligible: true);
        return rig;
    }

    /// <summary>An empty ledger and a seed row from before the ledger existed, stored under the authored root.</summary>
    public static SweepRig WithEarlierSeedRow()
    {
        var rig = new SweepRig([], FinderOver(EarlierSeedRow), gated: false);
        rig.Catalog.AddRow(FirstOldMediaId, SafeLibraryId, EarlierSeedRow.Locator, eligible: true);
        return rig;
    }

    static AdminMediaDto UploadedRow => Row(900, "/media/imaging/station-id.wav");

    static AdminMediaDto EarlierSeedRow => Row(FirstOldMediaId, $"{AuthoredRoot}/seed.wav");

    static AdminMediaDto Row(long id, string locator) => new(
        MediaId: id.ToString(), Locator: locator, Format: "wav", State: "ready", DurationMs: 5000,
        Title: SafeLoopSeeder.SeedTitle, Artist: "Test Station", Album: null, Genre: null, Year: null,
        IntegratedLufs: -16.0, TruePeakDbtp: -1.0, Measurable: true, CueInSec: null, CueOutSec: null,
        Eligible: true, Version: "1");

    static SeedRowFinder FinderOver(AdminMediaDto row)
    {
        var library = new FakeSafeLibraryQuery(SafeLibraryId, [row]);
        var station = new StationOptions { Safe = new StationSafeOptions { AuthoredRoot = AuthoredRoot } };
        return new SeedRowFinder(library, library, new FakeOptionsMonitor<StationOptions>(station));
    }
}
