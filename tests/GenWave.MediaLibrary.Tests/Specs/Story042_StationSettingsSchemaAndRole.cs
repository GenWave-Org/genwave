// STORY-042 — Station settings overlay store (the schema/role half)
//
// BDD specification — xUnit. Integration: real Postgres via DatabaseCollection. Verifies the new
// station schema + station_svc role + station.settings table created by
// db/06-station-settings-migration.sh. Mirrors Story030_EnergyColumnsSchemaAndMigration.
// The IConfigurationProvider/live-reload half lives in GenWave.Host.Tests.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureStationSettingsSchemaAndRole
{
    // ---------------------------------------------------------------------
    // Helpers  — all assertions via pg_catalog / information_schema which are
    // readable by library_svc (no cross-schema SELECT on station.settings).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns true if the named Postgres role exists.
    /// </summary>
    static async Task<bool> RoleExistsAsync(DatabaseFixture db, string roleName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = @role)",
            new { role = roleName });
    }

    /// <summary>
    /// Returns true if the named schema exists in the current database.
    /// </summary>
    static async Task<bool> SchemaExistsAsync(DatabaseFixture db, string schemaName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = @schema)",
            new { schema = schemaName });
    }

    /// <summary>
    /// Returns the schema owner as a role name.
    /// </summary>
    static async Task<string> SchemaOwnerAsync(DatabaseFixture db, string schemaName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<string>(
            @"SELECT pg_catalog.pg_get_userbyid(nspowner)
              FROM pg_catalog.pg_namespace
              WHERE nspname = @schema",
            new { schema = schemaName }) ?? string.Empty;
    }

    /// <summary>
    /// Returns the search_path GUC value set for the named role, or null when no setting exists.
    /// pg_db_role_setting.setconfig is an array of 'key=value' strings.
    /// </summary>
    static async Task<string?> RoleSearchPathAsync(DatabaseFixture db, string roleName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        // setdatabase = 0 means the role-level default (not tied to a specific DB).
        var configs = await conn.QueryFirstOrDefaultAsync<string[]?>(
            @"SELECT s.setconfig
              FROM pg_catalog.pg_db_role_setting s
              JOIN pg_catalog.pg_roles r ON r.oid = s.setrole
              WHERE r.rolname = @role
                AND s.setdatabase = 0",
            new { role = roleName });

        if (configs is null) return null;
        foreach (var cfg in configs)
        {
            if (cfg.StartsWith("search_path=", StringComparison.OrdinalIgnoreCase))
                return cfg["search_path=".Length..];
        }
        return null;
    }

    /// <summary>
    /// Returns (type_name, not_null, has_default) for a column in station.settings via
    /// pg_catalog (readable by any role — information_schema.columns only shows objects the
    /// current user has privilege on, so library_svc cannot see station schema objects there).
    /// Returns null when the column does not exist.
    /// </summary>
    static async Task<(string TypeName, bool NotNull, bool HasDefault)?> QuerySettingsColumnAsync(
        DatabaseFixture db, string columnName)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string type_name, bool not_null, bool has_default)>(
            @"SELECT pg_catalog.format_type(a.atttypid, a.atttypmod) AS type_name,
                     a.attnotnull                                      AS not_null,
                     a.atthasdef                                       AS has_default
              FROM pg_catalog.pg_attribute   a
              JOIN pg_catalog.pg_class       c ON c.oid = a.attrelid
              JOIN pg_catalog.pg_namespace   n ON n.oid = c.relnamespace
              WHERE n.nspname  = 'station'
                AND c.relname  = 'settings'
                AND a.attname  = @col
                AND a.attnum   > 0
                AND NOT a.attisdropped",
            new { col = columnName });

        return row == default ? null : (row.type_name, row.not_null, row.has_default);
    }

    /// <summary>
    /// Runs db/06-station-settings-migration.sh inside the test container via the fixture.
    /// The db-compose.yaml exposes STATION_DB_PASSWORD; see its environment block.
    /// </summary>
    static void RunMigrationScript(DatabaseFixture db)
    {
        var scriptPath = Path.Combine(db.RepoRoot, "db", "06-station-settings-migration.sh");
        db.RunFileInContainer(scriptPath);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioStationSchemaAndTableExist(DatabaseFixture db)
    {
        [Fact]
        public async Task StationSchemaExistsOwnedByStationSvc()
        {
            var exists = await SchemaExistsAsync(db, "station");
            Assert.True(exists, "station schema must exist after migration");

            var owner = await SchemaOwnerAsync(db, "station");
            Assert.Equal("station_svc", owner);
        }

        [Fact]
        public async Task StationSvcRoleExists()
        {
            var exists = await RoleExistsAsync(db, "station_svc");
            Assert.True(exists, "station_svc role must exist after migration");
        }

        [Fact]
        public async Task StationSettingsTableHasKeyColumn()
        {
            var col = await QuerySettingsColumnAsync(db, "key");
            Assert.NotNull(col);
            Assert.Equal("text", col.Value.TypeName);
            Assert.True(col.Value.NotNull, "key must be NOT NULL");
        }

        [Fact]
        public async Task StationSettingsTableHasValueColumn()
        {
            var col = await QuerySettingsColumnAsync(db, "value");
            Assert.NotNull(col);
            Assert.Equal("jsonb", col.Value.TypeName);
            Assert.True(col.Value.NotNull, "value must be NOT NULL");
        }

        [Fact]
        public async Task StationSettingsTableHasUpdatedAtColumn()
        {
            var col = await QuerySettingsColumnAsync(db, "updated_at");
            Assert.NotNull(col);
            Assert.Equal("timestamp with time zone", col.Value.TypeName);
            Assert.True(col.Value.NotNull, "updated_at must be NOT NULL");
            Assert.True(col.Value.HasDefault, "updated_at must have a default (now())");
        }

        [Fact]
        public async Task StationSvcSearchPathIsPinnedToStation()
        {
            var searchPath = await RoleSearchPathAsync(db, "station_svc");
            Assert.NotNull(searchPath);
            Assert.Contains("station", searchPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — idempotency
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrationIsIdempotent(DatabaseFixture db)
    {
        [Fact]
        public async Task RerunningTheMigrationDoesNotErrorOrChangeAnything()
        {
            // Schema and role already exist (created by 06-station-settings-migration.sh on boot).
            var schemaBefore = await SchemaExistsAsync(db, "station");
            var roleBefore   = await RoleExistsAsync(db, "station_svc");
            Assert.True(schemaBefore);
            Assert.True(roleBefore);

            // First explicit run.
            RunMigrationScript(db);

            // Second run — must also exit 0 (the DO block and IF NOT EXISTS guards are the fix).
            RunMigrationScript(db);

            // Schema and role are still there, unchanged.
            Assert.True(await SchemaExistsAsync(db, "station"));
            Assert.True(await RoleExistsAsync(db, "station_svc"));

            var keyCol = await QuerySettingsColumnAsync(db, "key");
            Assert.NotNull(keyCol);
            Assert.Equal("text", keyCol.Value.TypeName);
        }
    }
}
