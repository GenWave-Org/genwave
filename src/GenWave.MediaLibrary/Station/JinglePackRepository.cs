using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// The in-process implementation of <see cref="IJinglePackStore"/> (SPEC F165.2/F165.5/F165.6,
/// STORY-399/401, PLAN T414) over <c>station.jingle_pack</c> + <c>library.media</c>. Unlike every
/// other <c>*PackRepository</c> in this namespace, this one holds TWO <see cref="Lazy{T}"/>
/// <see cref="NpgsqlDataSource"/> fields — <paramref name="stationDataSource"/> and
/// <paramref name="libraryDataSource"/> — because its own two tables live behind two different
/// Postgres roles with no cross-schema grant between them (db/22; see
/// <see cref="IJinglePackStore"/>'s own remarks, and <see cref="JinglePackUpsertResult"/>'s/
/// <see cref="JinglePackDeleteResult"/>'s own "Honest boundary" remarks, for the full citation trail
/// back to db/42's and db/45's own header text). Every write below is therefore a GUARD-READ, THEN
/// WRITE sequence across two connections, never a single cross-schema statement or transaction —
/// documented per-method below.
/// </summary>
sealed class JinglePackRepository(
    Lazy<NpgsqlDataSource> stationDataSource,
    Lazy<NpgsqlDataSource> libraryDataSource,
    ILogger<JinglePackRepository> logger) : IJinglePackStore
{
    // Every ad_spot state a live bed_media_id reference can still be acted on from (db/42's own
    // station.ad_state enum) — mirrors VoicePackRepository.ActiveAdSpotStates verbatim, one schema
    // read over: 'failed'/'retired' spots are dead ends a pack write/uninstall must never be blocked
    // by.
    const string ActiveAdSpotStates = "'draft', 'approved', 'rendering', 'ready'";

    /// <summary>
    /// The ONE guard-read query lands its own <c>bed_media_id</c> reference check — both
    /// <see cref="UpsertAsync"/> (guarding a reinstall's dropped titles) and <see cref="DeleteAsync"/>
    /// (guarding the whole pack) run it, against their own candidate media id set, rather than each
    /// carrying its own inline copy: a mutation weakening the active-state set, or the
    /// <c>bed_media_id</c> column it joins on, has nowhere else to hide and breaks both call sites at
    /// once. <c>internal</c> — <c>InternalsVisibleTo</c> already grants
    /// <c>GenWave.MediaLibrary.Tests</c> (see this project's own .csproj), which pins this text
    /// directly (T413 review round 1 finding F3's own discipline, adapted: there is no single
    /// guarded DELETE statement to pin here, so this shared guard-read field is the next best
    /// single-source-of-truth to pin instead).
    /// </summary>
    internal static readonly string ActiveBedReferencesSql = $"""
        select distinct s.id
        from station.ad_spot s
        where s.bed_media_id = any(@mediaIds) and s.state in ({ActiveAdSpotStates})
        order by s.id
        """;

    internal static readonly string DeleteMediaSql = """
        delete from library.media
        where pack_slug = @slug
        returning path
        """;

    internal static readonly string DeletePackSql = """
        delete from station.jingle_pack
        where slug = @slug
        """;

    /// <summary>
    /// Backs <see cref="ListAsync"/> (SPEC F169.2, STORY-404, PLAN T419) — every installed jingle
    /// pack's slug and raw <c>definition</c> jsonb text, ordered by slug so the attributions endpoint
    /// need not re-sort. <c>internal</c> — pinned directly by
    /// <c>GenWave.MediaLibrary.Tests</c>'s <c>Story404_PackListSql.cs</c> (no Postgres needed to catch
    /// a dropped <c>order by</c>).
    /// </summary>
    internal static readonly string ListSql = """
        select slug, definition::text as definition_json
        from station.jingle_pack
        order by slug
        """;

    public async Task<IReadOnlyList<string>> FindPathsForPackAsync(string slug, CancellationToken ct)
    {
        await using var conn = await libraryDataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "select path from library.media where pack_slug = @slug",
            new { slug },
            cancellationToken: ct));

        return rows.ToList();
    }

    /// <summary>
    /// See <see cref="IJinglePackStore.UpsertAsync"/>'s own remarks for the upsert contract. The
    /// two-connection ordering here is STATION-first, with compensation: the STATION-side
    /// <c>station.jingle_pack</c> upsert runs FIRST (its previous row, if any, saved beforehand); the
    /// LIBRARY-side write (drop-then-upsert, one real <c>library_svc</c> transaction) runs SECOND. If
    /// the library write throws, the station row is compensated back to whatever it held before this
    /// call — restored from the saved previous row, or deleted if there was none — before the ORIGINAL
    /// exception is rethrown. This is the safer of the two possible orderings: the reverse (library
    /// first) risks a station row that still describes the OLD install while the library rows already
    /// carry the NEW one — if a caller's own unwind then restores the old bytes onto disk (as
    /// <c>JinglePackController.UnwindWritten</c> does), the rows would carry new size/mtime/loudness
    /// metadata pointing at bytes that no longer exist at those values (SPEC F165.5's own "the whole
    /// pack write is one unit" contract, broken silently). Station-first-with-compensation keeps that
    /// contract even when the library write fails outright: nothing durably promises an install that
    /// the library rows do not actually reflect.
    /// </summary>
    public async Task<JinglePackUpsertResult> UpsertAsync(
        string slug, string definition, string importedFrom,
        IReadOnlyList<JinglePackAssetInput> assets, CancellationToken ct)
    {
        var newTitles = assets.Select(a => a.Media.Tags.Title).ToHashSet(StringComparer.Ordinal);

        await using var libraryConn = await libraryDataSource.Value.OpenConnectionAsync(ct);
        var existing = (await libraryConn.QueryAsync<(long Id, string Title)>(new CommandDefinition(
            "select id, title from library.media where pack_slug = @slug",
            new { slug },
            cancellationToken: ct))).ToList();

        var droppedIds = existing.Where(r => !newTitles.Contains(r.Title)).Select(r => r.Id).ToArray();

        await using var stationConn = await stationDataSource.Value.OpenConnectionAsync(ct);

        if (droppedIds.Length > 0)
        {
            var referencingSpotIds = (await stationConn.QueryAsync<long>(new CommandDefinition(
                ActiveBedReferencesSql,
                new { mediaIds = droppedIds },
                cancellationToken: ct))).ToList();

            if (referencingSpotIds.Count > 0)
            {
                logger.LogWarning(
                    "Jingle pack upsert {Slug} refused: reinstall would drop a title still referenced by {Count} active ad spot(s)",
                    LogSanitize.Strip(slug), referencingSpotIds.Count);
                return new JinglePackUpsertResult.Refused(referencingSpotIds);
            }
        }

        var previousPack = await stationConn.QuerySingleOrDefaultAsync<PreviousPackRow?>(new CommandDefinition(
            "select definition::text as definition, imported_from, imported_at from station.jingle_pack where slug = @slug",
            new { slug },
            cancellationToken: ct));

        await stationConn.ExecuteAsync(new CommandDefinition(
            """
            insert into station.jingle_pack (slug, definition, imported_from, imported_at)
            values (@slug, @definition::jsonb, @importedFrom, now())
            on conflict (slug) do update
              set definition = @definition::jsonb,
                  imported_from = @importedFrom,
                  imported_at = now()
            """,
            new { slug, definition, importedFrom },
            cancellationToken: ct));

        var mediaIds = new List<long>(assets.Count);
        try
        {
            await using var tx = await libraryConn.BeginTransactionAsync(ct);

            if (droppedIds.Length > 0)
            {
                await libraryConn.ExecuteAsync(new CommandDefinition(
                    "delete from library.media where pack_slug = @slug and id = any(@droppedIds)",
                    new { slug, droppedIds },
                    transaction: tx,
                    cancellationToken: ct));
            }

            foreach (var asset in assets)
            {
                var id = await libraryConn.ExecuteScalarAsync<long>(new CommandDefinition(
                    AssetUpsertSql,
                    AssetUpsertParameters(slug, asset),
                    transaction: tx,
                    cancellationToken: ct));
                mediaIds.Add(id);
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception)
        {
            // Compensate on CancellationToken.None: a cancelled outer ct must not also abort the
            // compensation that undoes the station row this same call already committed.
            if (previousPack is { } previous)
            {
                await stationConn.ExecuteAsync(new CommandDefinition(
                    """
                    update station.jingle_pack
                    set definition = @definition::jsonb, imported_from = @importedFrom, imported_at = @importedAt
                    where slug = @slug
                    """,
                    new { slug, definition = previous.Definition, importedFrom = previous.ImportedFrom, importedAt = previous.ImportedAt },
                    cancellationToken: CancellationToken.None));
            }
            else
            {
                await stationConn.ExecuteAsync(new CommandDefinition(
                    "delete from station.jingle_pack where slug = @slug",
                    new { slug },
                    cancellationToken: CancellationToken.None));
            }

            logger.LogError(
                "Jingle pack upsert {Slug} failed after its station row committed; station.jingle_pack was compensated back to its previous state",
                LogSanitize.Strip(slug));
            throw;
        }

        return new JinglePackUpsertResult.Upserted(mediaIds);
    }

    /// <summary>The station-side <c>station.jingle_pack</c> row as it stood BEFORE this
    /// <see cref="UpsertAsync"/> call, read back with <c>QuerySingleOrDefaultAsync</c> so a fresh
    /// slug's "no previous row" case maps to <see langword="null"/> rather than an empty-record
    /// sentinel — the compensation step branches on that null directly.</summary>
    sealed record PreviousPackRow(string Definition, string ImportedFrom, DateTime ImportedAt);

    internal static readonly string AssetUpsertSql = """
        insert into library.media (
          path, format, size_bytes, mtime, state, library_id,
          duration_ms, sample_rate, channels, bitrate_kbps,
          title, artist, tags_edited_at,
          integrated_lufs, true_peak_dbtp, measurable,
          cue_in_sec, cue_out_sec, cue_analyzed_at,
          intro_energy, outro_energy, energy_analyzed_at,
          bpm_analyzed_at, year_lookup_at, year_lookup_missed_at, enriched_at,
          imaging_kind, pack_slug, jingle_role, eligible
        ) values (
          @path, @format, @sizeBytes, @mtime, 'ready', @libraryId,
          @durationMs, @sampleRate, @channels, @bitrateKbps,
          @title, @artist, now(),
          @integratedLufs, @truePeakDbtp, @measurable,
          @cueInSec, @cueOutSec, now(),
          @introEnergy, @outroEnergy, now(),
          now(), now(), now(), now(),
          @imagingKind, @packSlug, @jingleRole, true
        )
        on conflict (pack_slug, title) do update set
          path = excluded.path,
          format = excluded.format,
          size_bytes = excluded.size_bytes,
          mtime = excluded.mtime,
          state = 'ready',
          duration_ms = excluded.duration_ms,
          sample_rate = excluded.sample_rate,
          channels = excluded.channels,
          bitrate_kbps = excluded.bitrate_kbps,
          artist = excluded.artist,
          integrated_lufs = excluded.integrated_lufs,
          true_peak_dbtp = excluded.true_peak_dbtp,
          measurable = excluded.measurable,
          cue_in_sec = excluded.cue_in_sec,
          cue_out_sec = excluded.cue_out_sec,
          cue_analyzed_at = now(),
          intro_energy = excluded.intro_energy,
          outro_energy = excluded.outro_energy,
          energy_analyzed_at = now(),
          jingle_role = excluded.jingle_role,
          enriched_at = now()
        returning id
        """;

    static object AssetUpsertParameters(string slug, JinglePackAssetInput asset)
    {
        var media = asset.Media;
        return new
        {
            path = media.Path,
            format = media.Format,
            sizeBytes = media.SizeBytes,
            mtime = media.Mtime,
            libraryId = media.LibraryId,
            durationMs = media.DurationMs,
            sampleRate = media.SampleRate,
            channels = media.Channels,
            bitrateKbps = media.BitrateKbps,
            title = media.Tags.Title,
            artist = media.Tags.Artist,
            integratedLufs = media.Loudness.IntegratedLufs,
            truePeakDbtp = media.Loudness.TruePeakDbtp,
            measurable = media.Loudness.Measurable,
            cueInSec = media.Cue?.CueInSec,
            cueOutSec = media.Cue?.CueOutSec,
            introEnergy = media.Energy?.IntroEnergy,
            outroEnergy = media.Energy?.OutroEnergy,
            imagingKind = ImagingKindTokens.ToToken(media.Kind),
            packSlug = slug,
            jingleRole = asset.Role,
        };
    }

    /// <summary>
    /// See <see cref="IJinglePackStore.DeleteAsync"/>'s own remarks. The guard-read
    /// (<see cref="ActiveBedReferencesSql"/>) always runs, against the pack's OWN current asset ids,
    /// before either delete statement — <see cref="DeleteMediaSql"/> and <see cref="DeletePackSql"/>
    /// both carry no guard clause of their own (there is nothing for either to check across the
    /// schema boundary in a single statement; see this type's own remarks). The LIBRARY delete runs
    /// before the STATION delete — the opposite order from <see cref="UpsertAsync"/>'s own
    /// station-first-with-compensation, and deliberately so: an uninstall has no "new bytes" to
    /// protect the way a reinstall does, so the safer failure mode here is the plain one — a failure
    /// between the two deletes leaves an orphaned <c>station.jingle_pack</c> row naming a pack with
    /// zero remaining assets, self-healing on the next reinstall of that slug — rather than an
    /// orphaned <c>library.media</c> row set with no owning pack.
    /// </summary>
    public async Task<JinglePackDeleteResult> DeleteAsync(string slug, CancellationToken ct)
    {
        await using var stationConn = await stationDataSource.Value.OpenConnectionAsync(ct);
        var packExists = await stationConn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "select id from station.jingle_pack where slug = @slug",
            new { slug },
            cancellationToken: ct)) is not null;

        if (!packExists)
            return new JinglePackDeleteResult.NotFound();

        await using var libraryConn = await libraryDataSource.Value.OpenConnectionAsync(ct);
        var mediaIds = (await libraryConn.QueryAsync<long>(new CommandDefinition(
            "select id from library.media where pack_slug = @slug",
            new { slug },
            cancellationToken: ct))).ToArray();

        if (mediaIds.Length > 0)
        {
            var referencingSpotIds = (await stationConn.QueryAsync<long>(new CommandDefinition(
                ActiveBedReferencesSql,
                new { mediaIds },
                cancellationToken: ct))).ToList();

            if (referencingSpotIds.Count > 0)
            {
                logger.LogWarning(
                    "Jingle pack uninstall {Slug} refused: {Count} active ad spot(s) still reference its background music",
                    LogSanitize.Strip(slug), referencingSpotIds.Count);
                return new JinglePackDeleteResult.Refused(referencingSpotIds);
            }
        }

        var paths = (await libraryConn.QueryAsync<string>(new CommandDefinition(
            DeleteMediaSql,
            new { slug },
            cancellationToken: ct))).ToList();

        await stationConn.ExecuteAsync(new CommandDefinition(DeletePackSql, new { slug }, cancellationToken: ct));

        return new JinglePackDeleteResult.Deleted(paths);
    }

    public async Task<IReadOnlyList<InstalledPackDefinition>> ListAsync(CancellationToken ct)
    {
        await using var conn = await stationDataSource.Value.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<InstalledPackDefinition>(new CommandDefinition(
            ListSql, cancellationToken: ct));

        return rows.ToList();
    }
}
