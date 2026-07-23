// STORY-183 — Disclosure contract asserts complete property sets
//
// BDD specification — xUnit (SPEC F67.6; amends the F62.9 absence-style tests). Every public
// spectator DTO is enumerated below with its complete, camelCase-serialized property set — the
// blessed contract IS this table. A new field on any payload, or a brand-new Spectator-prefixed
// public type in GenWave.Host.Api, fails until this file is updated to bless it.

using System.Reflection;
using System.Text.Json;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

public static class FeatureDisclosureContractCompleteness
{
    /// <summary>
    /// The engine STORY-183 exists to enforce: serializes an instance exactly the way the app's
    /// controllers do — System.Text.Json with ASP.NET Core MVC's camelCase default
    /// (<c>Program.cs</c> registers no custom <c>JsonOptions</c>, so
    /// <see cref="JsonSerializerDefaults.Web"/> is the faithful stand-in) — then diffs the
    /// emitted property names against a blessed set. Returns violations rather than only
    /// throwing, so both the happy-path assertion (<see cref="ScenarioExactShapesPinned"/>) and
    /// its negative regression (<see cref="SadPathUnblessedField"/>) can inspect the same list.
    /// </summary>
    static class DisclosureContract
    {
        static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        public static IReadOnlyList<string> Violations(object instance, IReadOnlyCollection<string> blessedProperties)
        {
            var json = JsonSerializer.Serialize(instance, instance.GetType(), SerializerOptions);
            using var document = JsonDocument.Parse(json);
            var actual = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            var blessed = blessedProperties.ToHashSet(StringComparer.Ordinal);

            var violations = new List<string>();
            violations.AddRange(actual.Except(blessed)
                .Select(name => $"unexpected property '{name}' is not in the blessed contract"));
            violations.AddRange(blessed.Except(actual)
                .Select(name => $"blessed property '{name}' is missing from the payload"));
            return violations;
        }
    }

    /// <summary>
    /// One row per public spectator DTO: a representative instance (property values are
    /// arbitrary — only names/shape matter) paired with its complete, camelCase-serialized
    /// property set. This table IS the disclosure contract (SPEC F67.6) — a new field on any of
    /// these payloads must show up here before it can ship.
    /// </summary>
    sealed record BlessedShape(Type DtoType, object Instance, string[] Properties);

    static readonly BlessedShape[] BlessedShapes =
    [
        // now-playing (SPEC F62.4; amended 2026-07-20 to carry listeners — STORY-179)
        new(typeof(SpectatorTrackNowPlaying),
            new SpectatorTrackNowPlaying("Night Drive", "The Waveforms", DateTimeOffset.UtcNow, 214_000, 12),
            ["title", "artist", "startedAt", "durationMs", "listeners", "state", "kind"]),
        new(typeof(SpectatorPatterNowPlaying),
            new SpectatorPatterNowPlaying(DateTimeOffset.UtcNow, 9_000, 12),
            ["startedAt", "durationMs", "listeners", "state", "kind"]),
        new(typeof(SpectatorStandbyNowPlaying),
            new SpectatorStandbyNowPlaying(12),
            ["listeners", "state"]),

        // stats (SPEC F62.7) — Unavailable/Playable stay excluded by construction (F62.9)
        new(typeof(SpectatorStats),
            new SpectatorStats(5, 3, 2),
            ["ready", "enriching", "failed"]),

        // about (SPEC F62.8, F65.3)
        new(typeof(SpectatorAbout),
            new SpectatorAbout("GenWave Radio", "2.0.0", "AGPL-3.0-or-later",
                "https://github.com/GenWave-Org/genwave", "https://demo.example/stream"),
            ["stationName", "version", "license", "projectUrl", "streamUrl"]),

        // play-history (SPEC F62.6) — no media id, gain/loudness, or duration on either entry
        new(typeof(SpectatorPlayHistoryTrackEntry),
            new SpectatorPlayHistoryTrackEntry("Night Drive", "The Waveforms", DateTimeOffset.UtcNow),
            ["title", "artist", "airedAt", "kind"]),
        new(typeof(SpectatorPlayHistoryPatterEntry),
            new SpectatorPlayHistoryPatterEntry(DateTimeOffset.UtcNow),
            ["airedAt", "kind"]),
        new(typeof(SpectatorPlayHistoryResponse),
            new SpectatorPlayHistoryResponse([]),
            ["entries"]),

        // requests (SPEC F87.1, STORY-224, PLAN T87) — the 202 body is fixed/constant regardless
        // of wish content (no oracle); the submission DTO is blessed too so a future field bound
        // onto it (mass assignment) fails here first.
        new(typeof(SpectatorRequestAccepted),
            new SpectatorRequestAccepted(),
            ["status", "note"]),
        new(typeof(SpectatorRequestSubmission),
            new SpectatorRequestSubmission("more jazz please"),
            ["wish"]),
    ];

    /// <summary>
    /// Public Spectator-prefixed types in <c>GenWave.Host.Api</c> that are NOT serialized
    /// payloads — the controller and its marker attributes. Listed explicitly, rather than
    /// filtered out by some "looks like a DTO" heuristic, so the discovery assertion below still
    /// trips on a brand-new one of these until a person consciously adds it here.
    /// </summary>
    static readonly Type[] BlessedNonDtoTypes =
    [
        typeof(SpectatorController),
        typeof(SpectatorCacheControlAttribute),
        typeof(SpectatorSurfaceAttribute),
        // GET /spectator/api/artwork/{token} (SPEC F88.3, STORY-222, PLAN T84): serves a file
        // (jpeg or the station icon), never a JSON payload — nothing to bless as a DTO shape.
        typeof(SpectatorArtworkController),
        // POST /spectator/api/requests (SPEC F87, STORY-224, PLAN T87): the controller itself is
        // never serialized. Its kill-switch marker, RequestsSurfaceAttribute, is named "Requests*"
        // rather than "Spectator*" (unlike SpectatorSurfaceAttribute) so it falls outside this
        // scan's own Spectator-prefix filter below — nothing to bless for it here.
        typeof(SpectatorRequestsController),
    ];

    public static class ScenarioExactShapesPinned
    {
        [Fact]
        public static void Every_public_dto_matches_its_specced_shape_exactly()
        {
            // Given every spectator DTO type
            // When  the contract test reflects its serialized property names
            // Then  each matches the spec'd shape exactly, including listeners on
            //       now-playing per amended F62.4 (F67.6)
            Assert.All(BlessedShapes, shape =>
            {
                var violations = DisclosureContract.Violations(shape.Instance, shape.Properties);

                Assert.True(violations.Count == 0,
                    $"{shape.DtoType.Name}: {string.Join("; ", violations)}");
            });
        }

        [Fact]
        public static void No_spectator_prefixed_public_type_exists_outside_the_blessed_contract()
        {
            // Given the Host assembly
            // When  every public GenWave.Host.Api type named "Spectator*" is enumerated
            // Then  each one is either a blessed DTO shape above or a blessed non-DTO type — so
            //       a brand-new DTO (or attribute/controller) fails until it is blessed here
            var actualTypes = typeof(SpectatorController).Assembly.GetTypes()
                .Where(type => type.IsPublic
                    && type.Namespace == "GenWave.Host.Api"
                    && type.Name.StartsWith("Spectator", StringComparison.Ordinal))
                .ToHashSet();
            var blessedTypes = BlessedShapes.Select(shape => shape.DtoType)
                .Concat(BlessedNonDtoTypes)
                .ToHashSet();

            var unblessed = actualTypes.Except(blessedTypes).Select(type => type.Name).ToList();

            Assert.True(unblessed.Count == 0,
                $"unblessed Spectator-prefixed public type(s): {string.Join(", ", unblessed)}");
        }
    }

    public static class SadPathUnblessedField
    {
        /// <summary>
        /// A test-local shape carrying <see cref="SpectatorStats"/>'s exact fields plus one
        /// extra, never-blessed property. Not a literal C# subclass — every production DTO here
        /// is a <c>sealed record</c> and cannot be derived from — but the same shape with one
        /// field bolted on, which is what the contract helper actually needs to prove it catches
        /// (F67.6).
        /// </summary>
        sealed record UnblessedStatsWithExtraField(int Ready, int Enriching, int Failed, int Unavailable);

        [Fact]
        public static void Extra_property_fails_naming_the_unexpected_member()
        {
            // Given a test DTO derived from a public DTO with one extra property
            // When  the contract assertion runs against it
            // Then  it fails naming the unexpected property (F67.6)
            var blessed = BlessedShapes.Single(shape => shape.DtoType == typeof(SpectatorStats)).Properties;
            var instance = new UnblessedStatsWithExtraField(5, 3, 2, Unavailable: 7);

            var violations = DisclosureContract.Violations(instance, blessed);

            Assert.Contains(violations, violation => violation.Contains("unavailable", StringComparison.Ordinal));
        }
    }
}
