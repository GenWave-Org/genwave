using Npgsql;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// A disposable Postgres (fresh install: db/01 + db/06) for the repeat-view specs (STORY-542, STORY-545 AC8):
/// seeds personas, utterances and booth rows, and reads the two views back. Thin subclass of
/// <see cref="EphemeralStationDatabase"/>.
/// </summary>
internal sealed class RepeatViewsDatabase : EphemeralStationDatabase
{
    RepeatViewsDatabase(string project, string composeFile, string library, string station)
        : base(project, composeFile, library, station)
    {
    }

    public static async Task<RepeatViewsDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t708");
        var db = new RepeatViewsDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }

    /// <summary>Inserts a persona whose card holds <paramref name="definitionJson"/>; returns its id.</summary>
    public async Task<int> AddPersonaAsync(string name, string definitionJson = "{}")
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "insert into station.persona (name, definition) values (@name, @definition::jsonb) returning id", conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("definition", definitionJson);
        return (int)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("persona insert returned no id"));
    }

    public async Task AddUtteranceAsync(
        int personaId, string shiftKey, string text, bool fallback = false, TimeSpan? age = null)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            insert into station.utterance (persona_id, kind, segment_kind, shift_key, text, fallback, written_at)
            values (@persona, 'patter', 'LeadIn', @shift, @text, @fallback, now() - @age)
            """, conn);
        cmd.Parameters.AddWithValue("persona", personaId);
        cmd.Parameters.AddWithValue("shift", shiftKey);
        cmd.Parameters.AddWithValue("text", text);
        cmd.Parameters.AddWithValue("fallback", fallback);
        cmd.Parameters.AddWithValue("age", age ?? TimeSpan.Zero);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Writes a <c>repeat-caught</c> booth row the way BoothLogWriter words it.</summary>
    public async Task AddRepeatCaughtAsync(int personaId, string phrase, string outcome, TimeSpan? age = null)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            insert into station.booth_log (kind, summary, persona_id, occurred_at)
            values ('repeat-caught', @summary, @persona, now() - @age)
            """, conn);
        cmd.Parameters.AddWithValue("summary", $"Repeat caught: \"{phrase}\" — {outcome}");
        cmd.Parameters.AddWithValue("persona", personaId);
        cmd.Parameters.AddWithValue("age", age ?? TimeSpan.Zero);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Rows of <c>station.v_catchphrase_per_shift</c> for one persona, ordered by shift key.</summary>
    public async Task<IReadOnlyList<(string ShiftKey, string Phrase, int Occurrences)>> CatchphrasesAsync(int personaId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            select shift_key, phrase, occurrences from station.v_catchphrase_per_shift
            where persona_id = @persona order by shift_key, phrase
            """, conn);
        cmd.Parameters.AddWithValue("persona", personaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, string, int)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return rows;
    }

    /// <summary>The persona's row of <c>station.v_repeat_catches_7d</c>.</summary>
    public async Task<(int Catches, int Trims, int Fallbacks)> RepeatCatchesAsync(int personaId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select catches, trims, fallbacks from station.v_repeat_catches_7d where persona_id = @persona", conn);
        cmd.Parameters.AddWithValue("persona", personaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the view has no row for the persona");
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(StationConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
