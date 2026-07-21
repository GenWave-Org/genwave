// STORY-215 — Booth-log persona stamp (T60)
//
// BDD specification — xUnit (SPEC F84.6). Postgres-backed (Category=Integration, shared
// DatabaseFixture), same convention as Story195_BoothLogStore.cs: drives real TrackAired/
// SegmentGenerated/DegradationModeChanged events through the real BoothLogWriter/BoothLogDrainService
// pipeline into a real (test) database — the persona_id write, the FK's ON DELETE SET NULL degrade,
// the append-time FK-violation degrade, and the reader's projection are all real-DB behavior a fake
// store would never exercise honestly. The Host-side reader/DTO surface fact (proving the API
// projects the stamp) lives in GenWave.Host.Tests/Specs/Story215_TasteLearningGuardrails.cs instead —
// Host.Tests has no station Postgres by convention (see that file's own header).

using System.Threading.Channels;
using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBoothLogPersonaStamp
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static BoothLogRepository Store(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource),
            Microsoft.Extensions.Options.Options.Create(new BoothLogOptions()));

    static PersonaRepository PersonaRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    static async Task<long> CreatePersonaAsync(DatabaseFixture db, string name)
    {
        var result = await PersonaRepo(db).CreateAsync(new PersonaDraft(name, "", "", ""), CancellationToken.None);
        return Assert.IsType<PersonaWriteResult.Created>(result).Persona.Id;
    }

    static Persona APersona(long id, string name) => new(id, name, "", "", "", DateTime.UtcNow, DateTime.UtcNow);

    /// <summary>
    /// Scriptable <see cref="IActivePersonaAccessor"/> double — mirrors Story192_PersonaSchemaAndMigration's
    /// own fake of the same shape, with <see cref="Persona"/> settable so a scenario can flip the
    /// active persona mid-test (SPEC F84.6's "stamped at air time" claim is only provable if the
    /// answer can change AFTER <see cref="BoothLogWriter.Publish"/> already captured it).
    /// <see cref="ActivePersonaId"/> — the member <see cref="BoothLogWriter"/> actually calls — reads
    /// straight off the same <see cref="Persona"/> field <see cref="ResolveAsync"/> does, so both stay
    /// in lockstep with whatever the test last set.
    /// </summary>
    sealed class FakeActivePersonaAccessor(Persona? persona) : IActivePersonaAccessor
    {
        public Persona? Persona { get; set; } = persona;

        public Task<Persona?> ResolveAsync(CancellationToken ct) => Task.FromResult(Persona);

        public long? ActivePersonaId => Persona?.Id;
    }

    /// <summary>
    /// Publishes every <paramref name="events"/> through the real <see cref="BoothLogWriter"/> — which
    /// captures <paramref name="activePersona"/>'s id SYNCHRONOUSLY at publish time (F84.6) — and
    /// drains each through the real <see cref="BoothLogDrainService.ProcessAsync"/>, the same
    /// production pipeline Story195_BoothLogStore.cs's own scenario drives.
    /// </summary>
    static async Task DriveThroughAsync(DatabaseFixture db, Persona? activePersona, params StationEvent[] events)
    {
        var channel = Channel.CreateBounded<BoothLogEntryRequest>(16);
        var writer = new BoothLogWriter(
            channel.Writer, new FakeActivePersonaAccessor(activePersona), NullLogger<BoothLogWriter>.Instance);
        var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);

        foreach (var evt in events)
            writer.Publish(evt);

        for (var i = 0; i < events.Length; i++)
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);
    }

    sealed record PersonaIdRow(long? PersonaId);

    static async Task<List<PersonaIdRow>> AllPersonaIdsAsync(DatabaseFixture db)
    {
        await using var conn = await db.StationDataSource.OpenConnectionAsync();
        var rows = await conn.QueryAsync<PersonaIdRow>(
            "select persona_id::bigint as persona_id from station.booth_log order by occurred_at desc, id desc");
        return rows.ToList();
    }

    static TrackAired ATrackAiring() => new("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000);

    // ---------------------------------------------------------------------
    // HAPPY PATH — the active persona is stamped onto the track-start row (F84.6, AC2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTrackAiringUnderActivePersona(DatabaseFixture db)
    {
        [Fact]
        public async Task TrackStartRowIsStampedWithTheActivePersonaId()
        {
            // Given a persona active when a track airs...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var active = APersona(personaId, "Nova");

            // When the track-start event flows through the real writer/drain pipeline...
            await DriveThroughAsync(db, active, ATrackAiring());

            // Then the persisted row carries that persona's id — stamped at write time, never
            // inferred later.
            var rows = await AllPersonaIdsAsync(db);
            Assert.Equal([personaId], rows.Select(r => r.PersonaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a persona-less airing stays unstamped (F84.6)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaLessAiring(DatabaseFixture db)
    {
        [Fact]
        public async Task PersonaLessAiringWritesNullPersonaId()
        {
            // Given no active persona...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();

            // When a track airs...
            await DriveThroughAsync(db, activePersona: null, ATrackAiring());

            // Then the row's persona_id stays NULL — un-thumbable, not a missing/failed write.
            var rows = await AllPersonaIdsAsync(db);
            Assert.Equal([null], rows.Select(r => r.PersonaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — only track-start rows are stamp candidates (F84.6 scopes to "track rows")
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNonTrackRows(DatabaseFixture db)
    {
        [Fact]
        public async Task PatterAndModeChangeRowsAreNeverStamped()
        {
            // Given an active persona...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var active = APersona(personaId, "Nova");

            // When a patter air and a mode change flow through (neither is a track-start row)...
            await DriveThroughAsync(db, active,
                new SegmentGenerated("tts:abc123", "LeadIn", "af_heart"),
                new DegradationModeChanged("Normal", "Soft", "3 consecutive LLM failures (threshold 3)"));

            // Then neither row is stamped, even though a persona is active.
            var rows = await AllPersonaIdsAsync(db);
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Null(r.PersonaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the reader projects the stamp back (F84.6)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioReaderExposesStamp(DatabaseFixture db)
    {
        [Fact]
        public async Task ReadAsyncProjectsThePersonaStampOntoBoothLogEntry()
        {
            // Given one stamped row and one unstamped row...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var active = APersona(personaId, "Nova");

            await DriveThroughAsync(db, active, ATrackAiring());
            await DriveThroughAsync(db, activePersona: null,
                new TrackAired("7", "Static", "Nobody", -3.0, DateTimeOffset.UtcNow, 180_000));

            // When the admin feed reads the rows back...
            var page = await Store(db).ReadAsync(before: null, take: 10, CancellationToken.None);

            // Then each entry carries its own stamp (or null) — the reader exposes exactly what was
            // written, newest first.
            Assert.Equal([null, personaId], page.Entries.Select(e => e.PersonaId));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the stamp reflects AIR time, not DRAIN time, across a channel backlog (F84.6)
    //
    // This is the exact scenario the reviewer's finding described: resolving the active persona at
    // DRAIN time (rather than capturing it synchronously at PUBLISH time) mis-stamps a row that was
    // already queued behind a bounded-channel backlog once a persona switch lands before the drain
    // catches up. BoothLogWriter.Publish must have already captured the answer before this test ever
    // flips the active persona.
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaSwitchesDuringBacklog(DatabaseFixture db)
    {
        [Fact]
        public async Task QueuedTrackStartStaysStampedWithTheAirTimePersonaDespiteALaterSwitch()
        {
            // Given persona A active, and a track-start event published through the real writer —
            // captured synchronously, at air time, while A is still active...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaAId = await CreatePersonaAsync(db, "Nova");
            var personaBId = await CreatePersonaAsync(db, "Rook");
            var accessor = new FakeActivePersonaAccessor(APersona(personaAId, "Nova"));

            var channel = Channel.CreateBounded<BoothLogEntryRequest>(16);
            var writer = new BoothLogWriter(channel.Writer, accessor, NullLogger<BoothLogWriter>.Instance);
            var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);

            writer.Publish(ATrackAiring());

            // When the active persona switches to B BEFORE the drain ever processes the entry sitting
            // in the queue — the exact bounded-queue-backlog window the reviewer's finding described...
            accessor.Persona = APersona(personaBId, "Rook");
            await drain.ProcessAsync(await channel.Reader.ReadAsync(), CancellationToken.None);

            // Then the persisted row is stamped with A — the persona on air when the track STARTED,
            // never B, which only became active after the row had already queued.
            var rows = await AllPersonaIdsAsync(db);
            Assert.Equal([personaAId], rows.Select(r => r.PersonaId));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — deleting the stamped persona degrades an ALREADY-PERSISTED row, never deletes it
    // (ON DELETE SET NULL)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaDeleted(DatabaseFixture db)
    {
        [Fact]
        public async Task DeletingTheStampedPersonaDegradesTheRowToUnstamped()
        {
            // Given a track row stamped with a persona...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var active = APersona(personaId, "Nova");
            await DriveThroughAsync(db, active, ATrackAiring());

            // When that persona is deleted (ON DELETE SET NULL, not CASCADE — F84.6: booth-log
            // HISTORY rows must survive a persona deletion)...
            await PersonaRepo(db).DeleteAsync(personaId, CancellationToken.None);

            // Then the row survives, degraded to unstamped rather than removed.
            var rows = await AllPersonaIdsAsync(db);
            Assert.Single(rows);
            Assert.Null(rows[0].PersonaId);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the persona is deleted BEFORE the drain ever appends (not after) — the INSERT's own
    // FK violation (23503), not ON DELETE SET NULL, is what fires here. A new edge air-time capture
    // introduces: the stamp can go stale in the gap between publish and drain, not just after the row
    // already exists (F84.6).
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPersonaDeletedBetweenAirAndDrain(DatabaseFixture db)
    {
        [Fact]
        public async Task PersonaDeletedBeforeTheDrainAppendsDegradesTheInsertRatherThanDroppingTheRow()
        {
            // Given a persona active when a track airs — captured synchronously, at air time, into
            // the still-queued request...
            await db.ResetStationAsync();
            await db.ResetBoothLogAsync();
            var personaId = await CreatePersonaAsync(db, "Nova");
            var accessor = new FakeActivePersonaAccessor(APersona(personaId, "Nova"));

            var channel = Channel.CreateBounded<BoothLogEntryRequest>(16);
            var writer = new BoothLogWriter(channel.Writer, accessor, NullLogger<BoothLogWriter>.Instance);
            writer.Publish(ATrackAiring());
            var request = await channel.Reader.ReadAsync();
            Assert.Equal(personaId, request.PersonaId); // sanity: the air-time capture worked.

            // When that persona is deleted BEFORE the drain ever appends this row — the request still
            // carries the now-dangling id...
            await PersonaRepo(db).DeleteAsync(personaId, CancellationToken.None);

            // Then the append degrades persona_id to NULL and still writes the row: the FK violation
            // (23503) is caught and the insert retried unstamped, never dropped.
            var drain = new BoothLogDrainService(channel.Reader, Store(db), NullLogger<BoothLogDrainService>.Instance);
            await drain.ProcessAsync(request, CancellationToken.None);

            var rows = await AllPersonaIdsAsync(db);
            Assert.Single(rows);
            Assert.Null(rows[0].PersonaId);
        }
    }
}
