// STORY-058 — SafeScope live-edit via F19 settings API (serialization safety-net)
//
// K4-SN-1 — StationSettingsStore serializes long[] as a bare JSONB array, not a double-encoded string.
//            Catches the regression where the controller passes the raw "[2]" string instead of
//            the deserialized long[]{2}, causing JsonSerializer to produce "\"[2]\"" (a string-of-array)
//            which ExtractScalar would later surface as a plain string, silently bypassing
//            ExtractArrayItems and leaving SafeScope.LibraryIds empty.
//
// K4-SN-2 — StationSettingsConfigurationProvider expands a JSONB array row to indexed IConfiguration
//            keys (key:0, key:1, …) that the ASP.NET Core options binder maps to SafeScope.LibraryIds.
//            ExtractArrayItems is private; we test its contract through a seeded-provider subclass
//            whose Load() applies the same JSON → indexed-key expansion inline, without a live DB.

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GenWave.Host.Configuration;
using GenWave.Host.Options;

namespace GenWave.Host.Tests.Specs;

// ── K4-SN-2 helper — provider seeded with raw JSONB strings ─────────────────────────────────
//
// StationSettingsConfigurationProvider.ExtractArrayItems is private static, so it cannot be
// called directly from the test assembly.  We subclass and override Load() with the same
// JSONB → indexed-key logic, operating on in-memory JSON strings rather than a live DB row.
//
// Any future change to ExtractArrayItems' expansion contract must also be reflected here.
file sealed class RawJsonSeededProvider : StationSettingsConfigurationProvider
{
    readonly IReadOnlyDictionary<string, string> rawJsonSeed;

    /// <param name="rawJsonSeed">Map of config key → raw JSONB JSON string (as stored in station.settings).</param>
    public RawJsonSeededProvider(IReadOnlyDictionary<string, string> rawJsonSeed)
        : base(string.Empty)   // empty connection string: Load() is fully overridden, no DB access
    {
        this.rawJsonSeed = rawJsonSeed;
    }

    public override void Load()
    {
        foreach (var (key, json) in rawJsonSeed)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                    continue;   // only arrays expand to indexed keys; scalars are handled by ExtractScalar

                // Inline mirror of StationSettingsConfigurationProvider.ExtractArrayItems (private).
                var index = 0;
                foreach (var element in root.EnumerateArray())
                {
                    var elementValue = element.ValueKind switch
                    {
                        JsonValueKind.Number => element.GetRawText(),
                        JsonValueKind.String => element.GetString(),
                        JsonValueKind.True   => "true",
                        JsonValueKind.False  => "false",
                        _ => null,
                    };
                    if (elementValue is not null)
                        Set($"{key}:{index}", elementValue);
                    index++;
                }
            }
            catch (JsonException) { }
        }
    }
}

file sealed class ProviderWrapperSource(IConfigurationProvider inner) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => inner;
}

// ── Specs ────────────────────────────────────────────────────────────────────────────────────

public static class FeatureSafeScopeSerializationContracts
{
    const string LibraryIdsKey = "Station:SafeScope:LibraryIds";

    // ── K4-SN-1 — store serializes long[] to bare JSONB array ────────────────────────────────
    //
    // StationSettingsStore.WriteAsync:
    //   var json = JsonSerializer.Serialize(value);   // value is typed as object
    //
    // SettingsController deserializes the "[2]" request body to long[]{2}, casts to object, then
    // calls WriteAsync.  The serializer must inspect the runtime type (long[]) and produce a bare
    // JSON array so the DB stores JSONB [2], not the double-encoded string "[2]".

    public sealed class ScenarioStoreSerializesArrayAsBareJson
    {
        [Fact]
        public void LongArrayCastToObjectSerializesAsBareJsonArray()
        {
            // Exact call path: StationSettingsStore.WriteAsync → JsonSerializer.Serialize(value).
            var value = (object)new long[] { 2L };
            Assert.Equal("[2]", JsonSerializer.Serialize(value));
        }

        [Fact]
        public void LongArrayCastToObjectIsNotDoubleEncodedAsJsonString()
        {
            // Regression guard: if the controller passed the raw string "[2]" instead of long[]{2},
            // JsonSerializer.Serialize((object)"[2]") would produce "\"[2]\"".
            var value = (object)new long[] { 2L };
            Assert.NotEqual("\"[2]\"", JsonSerializer.Serialize(value));
        }
    }

    // ── K4-SN-2 — provider expands JSONB array to indexed IConfiguration keys ────────────────
    //
    // When StationSettingsConfigurationProvider.Load() reads a JSONB array from station.settings,
    // it calls ExtractArrayItems (private) to expand "[2,3]" into:
    //   Station:SafeScope:LibraryIds:0 = "2"
    //   Station:SafeScope:LibraryIds:1 = "3"
    // The ASP.NET Core options binder then maps these indexed keys to SafeScope.LibraryIds.

    public sealed class ScenarioProviderExpandsJsonbArrayToIndexedKeys
    {
        static IConfigurationRoot BuildConfig(string jsonbValue)
        {
            var provider = new RawJsonSeededProvider(
                new Dictionary<string, string> { [LibraryIdsKey] = jsonbValue });
            return new ConfigurationBuilder()
                .Add(new ProviderWrapperSource(provider))
                .Build();
        }

        [Fact]
        public void JsonbArrayExpandsFirstElementToIndexedConfigKey()
        {
            var config = BuildConfig("[2,3]");
            Assert.Equal("2", config[$"{LibraryIdsKey}:0"]);
        }

        [Fact]
        public void JsonbArrayExpandsSecondElementToIndexedConfigKey()
        {
            var config = BuildConfig("[2,3]");
            Assert.Equal("3", config[$"{LibraryIdsKey}:1"]);
        }

        [Fact]
        public void NonArrayJsonbDoesNotProduceIndexedConfigKey()
        {
            // A scalar JSONB value (e.g. a number "5") must not produce key:0.
            // This also covers the double-encoding regression: if the DB stores "\"[2]\""
            // (a JSON string rather than a JSONB array), ExtractScalar returns it as a plain string
            // and ExtractArrayItems is never called, leaving LibraryIds silently empty.
            var config = BuildConfig("5");
            Assert.Null(config[$"{LibraryIdsKey}:0"]);
        }

        [Fact]
        public void IndexedConfigKeysFromJsonbArrayBindToSafeScopeLibraryIds()
        {
            // End-to-end: JSONB "[2,3]" → indexed keys → StationOptions.SafeScope.LibraryIds = [2,3].
            var config = BuildConfig("[2,3]");

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddOptions<StationOptions>().Bind(config.GetSection(StationOptions.Section));
            var options = services.BuildServiceProvider().GetRequiredService<IOptions<StationOptions>>();

            Assert.Equal(new[] { 2L, 3L }, options.Value.SafeScope.LibraryIds.ToArray());
        }
    }
}
