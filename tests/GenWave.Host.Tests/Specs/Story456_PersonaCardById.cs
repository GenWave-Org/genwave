// STORY-456 — The speaker travels with the plan — a card by id (gh-#772 · SPEC F189.2 · PLAN T525)
//
// BDD specification — xUnit. AC10 drives the Host IPersonaCardByIdSource implementation against the Postgres fixture: a persona row whose
// card carries one pronunciation rule.
//
// WIRED T525. Entry-point discipline: drives the REAL production pieces — the same IPersonaStore
// PersonaServiceCollectionExtensions.AddPersonaStore wires into the Host, PersonaCardByIdStore (the
// Host adapter), and SpeakerSnapshotSource (GenWave.Tts) — against a real ephemeral Postgres
// (tests/GenWave.Host.Tests/Support/EphemeralStationDatabase, the Story367/Story369/Story370 idiom).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Support;
using GenWave.MediaLibrary.Station;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePersonaCardById
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(PersonaCardByIdCollection.Name)]
    public sealed class ScenarioAPersonaRowWithACard(PersonaCardByIdArc arc)
    {
        // Given: the card carries one pronunciation rule

        /// <summary>AC10 — the snapshot resolves the inserted persona's own id.</summary>
        [Fact]
        public void ResolvesTheCardById() => Assert.Equal(arc.InsertedPersonaId, arc.Snapshot.PersonaId);

        /// <summary>AC10 — the snapshot's merged rules carry the card's own rule.</summary>
        [Fact]
        public void TheSnapshotCarriesTheRule() =>
            Assert.Contains(arc.Snapshot.Rules, rule => rule.Word == "Nguyen");
    }
}

// ── Collection definition — one ephemeral Postgres shared across every Scenario above (the
// Story367/Story369/Story370 "one Arc, one InitializeAsync, Facts just assert" idiom). ────────────

[CollectionDefinition(Name)]
public sealed class PersonaCardByIdCollection : ICollectionFixture<PersonaCardByIdArc>
{
    public const string Name = "Story456PersonaCardById";
}

/// <summary>
/// Arranges ONE ephemeral station Postgres, seeds a persona row with a definition card (one
/// pronunciation rule), then resolves it through the REAL production chain: <see
/// cref="PersonaServiceCollectionExtensions.AddPersonaStore"/>'s own <see cref="IPersonaStore"/> →
/// <see cref="PersonaCardByIdStore"/> (the Host <see cref="IPersonaCardByIdSource"/> adapter) → <see
/// cref="SpeakerSnapshotSource"/> (<see cref="GenWave.Tts"/>'s <see cref="ISpeakerSnapshotSource"/>).
/// </summary>
public sealed class PersonaCardByIdArc : IAsyncLifetime
{
    public long InsertedPersonaId { get; private set; }
    public SpeakerSnapshot Snapshot { get; private set; } =
        new(null, null, "af_heart", TtsPace.EngineDefault, [], [], "");

    public async Task InitializeAsync()
    {
        await using var db = await PersonaCardByIdStationDatabase.StartAsync();

        var card = new PersonaCard(
            SchemaVersion: PersonaCard.CurrentSchemaVersion,
            Name: "Story456 Probe DJ",
            Tagline: "Probe DJ for the card-by-id seam",
            Soul: "Exists only to prove a card resolves by id",
            Quirks: [],
            Voice: new VoiceSpec("kokoro", "af_heart", 1.1, "en-US"),
            EnergyDisposition: 0.5,
            Lore: [],
            Corrections: [],
            Pronunciations: [new GenWave.Core.Domain.PronunciationRule("Nguyen", "Nguyen", "ˈwɪn")]);

        InsertedPersonaId = await InsertPersonaWithCardAsync(db.StationConnectionString, "Story456 Probe DJ", card);

        using var services = new ServiceCollection()
            .AddPersonaStore(db.StationConnectionString)
            .BuildServiceProvider();
        var personaStore = services.GetRequiredService<IPersonaStore>();

        var cardSource = new PersonaCardByIdStore(personaStore);
        var identityProvider = new GenWave.TestSupport.Fakes.FakeStationIdentityProvider(
            new StationIdentity("genwave-1", "Test", "af_heart"));
        var pronunciationRuleProvider = new PronunciationRuleProvider(
            new FakeOptionsMonitor<TtsPronunciationsOptions>(new TtsPronunciationsOptions()),
            NullLogger<PronunciationRuleProvider>.Instance);
        var speechCorrectionProvider = new SpeechCorrectionProvider(
            new FakeOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions()),
            NullLogger<SpeechCorrectionProvider>.Instance);

        var source = new SpeakerSnapshotSource(
            cardSource, identityProvider, pronunciationRuleProvider, speechCorrectionProvider,
            NullLogger<SpeakerSnapshotSource>.Instance);

        Snapshot = await source.ForPersonaAsync(InsertedPersonaId, CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static async Task<long> InsertPersonaWithCardAsync(string stationConnectionString, string name, PersonaCard card)
    {
        var definition = PersonaCardSerializer.Serialize(card);

        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "insert into station.persona (name, definition) values (@name, @definition::jsonb) returning id::bigint";
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("definition", definition);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}

file sealed class PersonaCardByIdStationDatabase : EphemeralStationDatabase
{
    PersonaCardByIdStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<PersonaCardByIdStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-cardbyid");
        var db = new PersonaCardByIdStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
