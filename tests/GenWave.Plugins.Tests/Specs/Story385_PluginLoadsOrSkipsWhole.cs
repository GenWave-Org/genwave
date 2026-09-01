// STORY-385 — A plugin loads from a mounted folder, or is skipped whole (F156 · pending T391/T392)
// Loader happy/sad paths run against throwaway plugin assemblies EMITTED AT TEST TIME (Roslyn)
// so CI stays hermetic — the real-world proof is the genwave-plugin-example repo DLL at T394.
//
// T391 GREEN below: manifest-driven discovery (ScenarioManifestDrivenDiscoveryOnly) and the pure
// parser's reject rules (ScenarioRejectingBadManifests) — both against real temp directories
// (Directory.CreateTempSubdirectory), never fakes. T392-tagged facts (the loader itself, against
// Roslyn-emitted assemblies) stay Skip'd for the next task.

namespace GenWave.Plugins.Tests.Specs;

using System.Text.Json;
using GenWave.Plugins;

public static class FeaturePluginLoadsOrSkipsWhole
{
    /// <summary>Builds well-formed <c>plugin.json</c> text, one field override at a time, and the
    /// "field entirely absent from the JSON body" variant every missing-field fact below needs — the
    /// one place this file's facts describe what a manifest document looks like, so a shape change
    /// never needs updating in more than one place.</summary>
    static class ManifestDocument
    {
        public static string WellFormed() => Build();

        public static string WithField(string field, string? value) => Build(new Dictionary<string, string?> { [field] = value });

        public static string Missing(string field)
        {
            var fields = DefaultFields();
            fields.Remove(field);
            return JsonSerializer.Serialize(fields);
        }

        static string Build(IReadOnlyDictionary<string, string?>? overrides = null)
        {
            var fields = DefaultFields();
            if (overrides is not null)
            {
                foreach (var (key, value) in overrides)
                    fields[key] = value;
            }

            return JsonSerializer.Serialize(fields);
        }

        static Dictionary<string, string?> DefaultFields() => new()
        {
            ["name"] = "Sample Plugin",
            ["version"] = "1.0.0",
            ["assembly"] = "SamplePlugin.dll",
            ["entryType"] = "Sample.EntryPoint",
            ["abstractions"] = "5.6.0",
        };
    }

    static PluginManifest AssertSuccess(PluginManifestParseResult result)
    {
        Assert.True(result.Succeeded);
        return result.Manifest;
    }

    static PluginManifestField AssertFailureField(PluginManifestParseResult result)
    {
        Assert.False(result.Succeeded);
        return result.Field;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioManifestDrivenDiscoveryOnly : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-plugins-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void OnlyManifestDirectoriesAreConsidered()
        {
            // Given a plugins root with <slug>/plugin.json AND a loose stray.dll beside it...
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "sample-plugin"));
            File.WriteAllText(Path.Combine(pluginDirectory.FullName, PluginManifestDiscovery.ManifestFileName), ManifestDocument.WellFormed());
            File.WriteAllText(Path.Combine(root, "stray.dll"), "not a real assembly");

            // When the loader enumerates...
            var candidates = PluginManifestDiscovery.EnumerateCandidates(root).ToList();

            // Then discovery yields exactly the manifest directory; the loose DLL is never probed.
            var candidate = Assert.Single(candidates);
            Assert.Equal("sample-plugin", candidate.Slug);
        }

        [Fact]
        public void CandidatesYieldInAscendingSlugOrder()
        {
            // Given three plugin directories created in a deliberately NON-alphabetical order...
            foreach (var slug in new[] { "zeta-plugin", "alpha-plugin", "mike-plugin" })
            {
                var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, slug));
                File.WriteAllText(Path.Combine(pluginDirectory.FullName, PluginManifestDiscovery.ManifestFileName), ManifestDocument.WellFormed());
            }

            // When discovery enumerates...
            var slugs = PluginManifestDiscovery.EnumerateCandidates(root).Select(c => c.Slug).ToList();

            // Then candidates yield in ascending StringComparer.Ordinal slug order (SPEC F156.6's
            // "earlier plugin" tiebreak), not filesystem enumeration order.
            Assert.Equal(new[] { "alpha-plugin", "mike-plugin", "zeta-plugin" }, slugs);
        }

        [Fact]
        public void ASymlinkedChildDirectoryYieldsNoCandidate()
        {
            // Given a real plugin directory, a symlink ALIASING it (a sibling impersonation attempt),
            // and a symlink pointing OUTSIDE the plugins root entirely (an escape attempt)...
            var realPlugin = Directory.CreateDirectory(Path.Combine(root, "real-plugin"));
            File.WriteAllText(Path.Combine(realPlugin.FullName, PluginManifestDiscovery.ManifestFileName), ManifestDocument.WellFormed());

            var outsideTarget = Directory.CreateTempSubdirectory("genwave-plugins-outside-");
            try
            {
                File.WriteAllText(Path.Combine(outsideTarget.FullName, PluginManifestDiscovery.ManifestFileName), ManifestDocument.WellFormed());

                Directory.CreateSymbolicLink(Path.Combine(root, "aliased-plugin"), realPlugin.FullName);
                Directory.CreateSymbolicLink(Path.Combine(root, "escaped-plugin"), outsideTarget.FullName);

                // When discovery enumerates...
                var slugs = PluginManifestDiscovery.EnumerateCandidates(root).Select(c => c.Slug).ToList();

                // Then only the real, non-symlinked directory is a candidate (gh-#650, fail-closed).
                Assert.Equal(new[] { "real-plugin" }, slugs);
            }
            finally
            {
                outsideTarget.Delete(recursive: true);
            }
        }
    }

    public static class ScenarioParsingAWellFormedManifest
    {
        [Fact]
        public static void AWellFormedManifestParsesAllFiveFields()
        {
            // Given a well-formed manifest document...
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WellFormed());

            // Then name/version/assembly/entryType/abstractions all round-trip from JSON.
            var manifest = AssertSuccess(result);
            Assert.Equal(
                ("Sample Plugin", "1.0.0", "SamplePlugin.dll", "Sample.EntryPoint", "5.6.0"),
                (manifest.Name, manifest.Version, manifest.AssemblyFileName, manifest.EntryType, manifest.Abstractions));
        }
    }

    public sealed class ScenarioAValidPluginLoadsInItsOwnContext
    {
        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void TheAssemblyLoadsInADedicatedLoadContext()
        {
            // Emit a minimal IGenWavePlugin assembly; load; its ALC is not Default and is per-plugin.
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void AbstractionsTypesUnifyWithTheHost()
        {
            // typeof(IGenWavePlugin) from the loaded plugin instance == the host's type identity
            //   (a plugin-carried Abstractions copy is never loaded — F156.3).
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void RegisterRunsAndItsRegistrationsAreCollected()
        {
            // The emitted plugin's Register(IPluginHost) adds one IContextProvider;
            //   the collector holds exactly that instance.
            Assert.Fail("pending T392");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — every failure skips the WHOLE plugin, boot continues (F156.4)
    // ---------------------------------------------------------------------

    public static class ScenarioRejectingBadManifests
    {
        [Fact]
        public static void AMissingEntryTypeSkipsWithAWarnNamingTheField()
        {
            // Given a manifest missing 'entryType'...
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.Missing("entryType"));

            // Then the field named in the (structured) reject reason is EntryType.
            Assert.Equal(PluginManifestField.EntryType, AssertFailureField(result));
        }

        [Fact]
        public static void AnAssemblyValueWithAPathSeparatorIsRejected()
        {
            // Given a manifest whose 'assembly' names a path, not a bare file name — "sub/dir.dll"...
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WithField("assembly", "sub/dir.dll"));

            // Then it refuses, naming the Assembly field.
            Assert.Equal(PluginManifestField.Assembly, AssertFailureField(result));
        }

        // The comment on the pending fact above named a SECOND shape too ("..\\up.dll") — pinned
        // here as its own fact, exhaustive reject pinning (the CrosstalkScriptParser precedent).

        [Fact]
        public static void AnAssemblyValueWithABackslashIsRejected()
        {
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WithField("assembly", "sub\\dir.dll"));

            Assert.Equal(PluginManifestField.Assembly, AssertFailureField(result));
        }

        [Fact]
        public static void AnAssemblyValueWithPathTraversalIsRejected()
        {
            // "..\\up.dll" — a traversal shape, no forward slash at all.
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WithField("assembly", "..\\up.dll"));

            Assert.Equal(PluginManifestField.Assembly, AssertFailureField(result));
        }

        [Fact]
        public static void ABareDoubleDotAssemblyIsRejectedEvenWithoutASeparator()
        {
            // ".." alone, mid-name, with no '/' or '\' anywhere — still a traversal shape.
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WithField("assembly", "..dll"));

            Assert.Equal(PluginManifestField.Assembly, AssertFailureField(result));
        }

        [Theory]
        [InlineData(".")]              // a dot-name: meaningless as a bare file name
        [InlineData("C:x.dll")]        // a Windows drive/NTFS-stream separator shape
        [InlineData("a b.dll")]        // embedded whitespace
        [InlineData("  S.dll  ")]      // leading/trailing whitespace
        public static void AStructurallyInvalidAssemblyFileNameIsRejected(string assembly)
        {
            // Given a manifest whose 'assembly' takes one of these structurally-invalid shapes...
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WithField("assembly", assembly));

            // Then it refuses, naming the Assembly field — exhaustive pinning of the structural rule
            // (PluginManifestParser.IsInvalidAssemblyFileName), not just the path/traversal shapes
            // ContainsPathSeparatorOrTraversal alone catches above.
            Assert.Equal(PluginManifestField.Assembly, AssertFailureField(result));
        }

        [Theory]
        [InlineData("name", PluginManifestField.Name)]
        [InlineData("version", PluginManifestField.Version)]
        [InlineData("assembly", PluginManifestField.Assembly)]
        [InlineData("entryType", PluginManifestField.EntryType)]
        [InlineData("abstractions", PluginManifestField.Abstractions)]
        public static void AManifestMissingAnyRequiredFieldSkipsNamingThatField(string missingField, PluginManifestField expected)
        {
            // Given a manifest with that one field entirely absent from the JSON body...
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.Missing(missingField));

            // Then the reject names exactly that field — exhaustive per-field pinning, not just the
            // one AC5-named example (entryType) above.
            Assert.Equal(expected, AssertFailureField(result));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public static void ABlankNameSkipsNamingTheField(string blank)
        {
            // A field PRESENT but blank is rejected the same as one entirely missing — the SPEC's
            // "missing or malformed" covers both shapes, not just outright absence.
            var result = PluginManifestParser.Parse("sample-plugin", ManifestDocument.WithField("name", blank));

            Assert.Equal(PluginManifestField.Name, AssertFailureField(result));
        }

        [Fact]
        public static void MalformedJsonIsRejectedAsTheWholeDocument()
        {
            var result = PluginManifestParser.Parse("sample-plugin", "{ this is not json");

            Assert.Equal(PluginManifestField.Document, AssertFailureField(result));
        }

        [Fact]
        public static void AnUppercaseKeyedManifestRejectsOnTheMissingLowercaseField()
        {
            // Given a manifest whose keys are all uppercase ("NAME" rather than "name")...
            var uppercased = "{\"NAME\":\"Sample Plugin\",\"VERSION\":\"1.0.0\",\"ASSEMBLY\":\"SamplePlugin.dll\"," +
                "\"ENTRYTYPE\":\"Sample.EntryPoint\",\"ABSTRACTIONS\":\"5.6.0\"}";
            var result = PluginManifestParser.Parse("sample-plugin", uppercased);

            // Then it rejects on the (now-missing, exact-case-only) 'name' field: PropertyNameCaseInsensitive
            // is deliberately NOT set (SPEC F156.2 names the five fields in lowercase), so "NAME" never
            // binds — the whole manifest is treated as if every field were entirely absent.
            Assert.Equal(PluginManifestField.Name, AssertFailureField(result));
        }

        [Fact]
        public static void AnAssemblyValueContainingCrLfProducesASingleLineDetail()
        {
            // Given a crafted 'assembly' value that both FAILS validation (an embedded path separator)
            // AND carries a raw CR/LF payload, positioned to land inside the reject message's own
            // interpolated value...
            var result = PluginManifestParser.Parse(
                "sample-plugin", ManifestDocument.WithField("assembly", "evil\r\nWARN forged/dir.dll"));

            // Then it still refuses, naming the Assembly field...
            Assert.Equal(PluginManifestField.Assembly, AssertFailureField(result));

            // ...but Detail is neutralized to a single line — CWE-117 log forging closed, without the
            // reject reason losing which field caused it.
            Assert.DoesNotContain('\r', result.Detail);
            Assert.DoesNotContain('\n', result.Detail);
        }

        [Fact]
        public static void AJsonPropertyNameContainingCrLfProducesASingleLineDetail()
        {
            // Given a malformed manifest whose (unrecognized, skipped) property NAME itself carries a
            // JSON-escaped CR/LF, positioned next to a syntax error so System.Text.Json's own
            // JsonException.Message echoes it back raw in the reported Path (proven at T391 review —
            // System.Text.Json does not sanitize its own exception text)...
            var result = PluginManifestParser.Parse("sample-plugin", "{\"evil\\r\\nprop\": {\"x\": }, \"name\":\"n\"}");

            // Then the whole document is rejected...
            Assert.Equal(PluginManifestField.Document, AssertFailureField(result));

            // ...and Detail is still a single line, despite embedding System.Text.Json's own raw
            // exception text.
            Assert.DoesNotContain('\r', result.Detail);
            Assert.DoesNotContain('\n', result.Detail);
        }
    }

    public sealed class ScenarioSkippingBrokenPlugins
    {
        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void ACorruptDllSkipsTheWholePluginAndBootContinues()
        {
            // Manifest whose assembly file is garbage bytes: zero registrations from that dir,
            //   one WARN naming the cause, the loader returns normally.
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void AThrowingRegisterLeavesNoPartialRegistrations()
        {
            // Plugin adds one provider then throws: the collector holds NOTHING from it.
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void AContextKeyCollisionSkipsTheColliderWhole()
        {
            // Emitted plugin whose IContextProvider.Key == "weather" (a built-in key):
            //   pre-validation skips that plugin entirely — ContextPipeline's fail-fast ctor
            //   must never be the thing that discovers the collision (F156.6).
            Assert.Fail("pending T392");
        }

        [Fact(Skip = "Pending T392 — see docs/PLAN.md")]
        public void TwoPluginsCollidingOnAKeyLoadFirstSkipSecond()
        {
            Assert.Fail("pending T392");
        }
    }
}
