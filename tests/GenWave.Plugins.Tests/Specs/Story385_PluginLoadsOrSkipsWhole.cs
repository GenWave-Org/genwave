// STORY-385 — A plugin loads from a mounted folder, or is skipped whole (F156 · T391+T392 GREEN)
// Loader happy/sad paths run against throwaway plugin assemblies EMITTED AT TEST TIME (Roslyn)
// so CI stays hermetic — the real-world proof is the genwave-plugin-example repo DLL at T394.
//
// T391 GREEN: manifest-driven discovery (ScenarioManifestDrivenDiscoveryOnly) and the pure
// parser's reject rules (ScenarioRejectingBadManifests) — both against real temp directories
// (Directory.CreateTempSubdirectory), never fakes.
//
// T392 GREEN: the loader itself (ScenarioAValidPluginLoadsInItsOwnContext,
// ScenarioSkippingBrokenPlugins) — against assemblies Support/EmittedPluginAssembly.cs compiles
// with Roslyn at test time, never a fixture project checked into the repo.

namespace GenWave.Plugins.Tests.Specs;

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Plugins;
using GenWave.Plugins.Tests.Support;

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

        /// <summary>Multiple overrides at once — the loader facts below (T392) need to override
        /// <c>assembly</c>/<c>entryType</c> together to match an actual emitted DLL, which the
        /// single-field <see cref="WithField"/> can't express.</summary>
        public static string WithFields(IReadOnlyDictionary<string, string?> overrides) => Build(overrides);

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

    /// <summary>C# source templates for the throwaway plugins <c>EmittedPluginAssembly</c> compiles
    /// (PLAN T392) — every template implements exactly one <c>IContextProvider</c> so a fact only ever
    /// needs to choose its provider's <c>Key</c> (or, for the sad-path template, that plus a throw).</summary>
    static class PluginSource
    {
        public static string SingleContextProvider(string key) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Test Plugin";

                public void Register(IPluginHost host) => host.AddContextProvider(new Provider());

                sealed class Provider : IContextProvider
                {
                    public string Key => "{{key}}";

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
                }
            }
            """;

        public static string ContextProviderThenThrowingRegister(string key) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Throwing Test Plugin";

                public void Register(IPluginHost host)
                {
                    host.AddContextProvider(new Provider());
                    throw new System.InvalidOperationException("Register intentionally throws for STORY-385 AC8.");
                }

                sealed class Provider : IContextProvider
                {
                    public string Key => "{{key}}";

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
                }
            }
            """;

        /// <summary>T392 review finding 2b: a plugin registering an <c>IAdSpotSource</c> instead of an
        /// <c>IContextProvider</c> — the OTHER contract <c>IPluginHost</c> accepts, needed to pin that
        /// the loader actually commits it (not just the context-provider path every other template
        /// here exercises).</summary>
        public static string SingleAdSpotSource() => """
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Ad Spot Test Plugin";

                public void Register(IPluginHost host) => host.AddAdSpotSource(new Source());

                sealed class Source : IAdSpotSource
                {
                    public ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken ct) => ValueTask.FromResult<MediaItem?>(null);
                }
            }
            """;

        /// <summary>T392 review finding 2c: ONE plugin registering TWO providers that share a key —
        /// distinct from the cross-plugin collision templates above, which never exercise the
        /// within-one-Register-call collision branch of <c>TryValidateContextProviderKeys</c>.</summary>
        public static string TwoContextProvidersSharingAKey(string key) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Duplicate Key Test Plugin";

                public void Register(IPluginHost host)
                {
                    host.AddContextProvider(new Provider());
                    host.AddContextProvider(new Provider());
                }

                sealed class Provider : IContextProvider
                {
                    public string Key => "{{key}}";

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
                }
            }
            """;

        /// <summary>T392 review finding 2a: <c>Register</c> throws an exception whose OWN
        /// <c>Message</c> carries a raw CR/LF payload — pins that <see cref="PluginLoadReport.Detail"/>
        /// still comes out single-line even when the CRAFTED text arrives via the exception path,
        /// not the manifest-parse path <c>ScenarioRejectingBadManifests</c>'s own CR/LF facts already
        /// cover.</summary>
        public static string RegisterThrowsWithCrLfMessage() => """
            using GenWave.Core.Abstractions;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Crlf Throwing Test Plugin";

                public void Register(IPluginHost host) =>
                    throw new System.InvalidOperationException("evil\r\nWARN forged/line");
            }
            """;

        /// <summary>T392 round-2 review finding B1: a provider whose <c>Key</c> GETTER throws —
        /// <c>IContextProvider</c>'s own "throwing is equally ordinary" posture applied to a member
        /// this loader itself reads (never just <c>FetchAsync</c>, the one member that interface's own
        /// docs name outright) — needed to prove the loader's outer safety net actually converts an
        /// unanticipated exception into a typed Skipped/Unexpected report rather than letting it
        /// escape <c>LoadAll</c>.</summary>
        public static string ContextProviderWithThrowingKeyGetter() => """
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Throwing Key Test Plugin";

                public void Register(IPluginHost host) => host.AddContextProvider(new Provider());

                sealed class Provider : IContextProvider
                {
                    public string Key => throw new System.InvalidOperationException("Key getter intentionally throws.");

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
                }
            }
            """;

        /// <summary>T392 round-2 review finding B2: a provider whose <c>Key</c> getter answers
        /// <paramref name="firstKey"/> on its FIRST call and <paramref name="secondKey"/> on every
        /// call after (a plain static counter, the drift shape the review named) — proves the loader
        /// validates a provider's key EXACTLY ONCE and commits that same value, never a second live
        /// read that could smuggle a different (potentially colliding) answer past validation.</summary>
        public static string ContextProviderWithDriftingKey(string firstKey, string secondKey) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public string Name => "Drifting Key Test Plugin";

                public void Register(IPluginHost host) => host.AddContextProvider(new Provider());

                sealed class Provider : IContextProvider
                {
                    static int readCount;

                    public string Key
                    {
                        get
                        {
                            readCount++;
                            return readCount == 1 ? "{{firstKey}}" : "{{secondKey}}";
                        }
                    }

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
                }
            }
            """;

        /// <summary>T392 review finding 3's own pin fixture: a plugin that RETAINS the
        /// <see cref="IPluginHost"/> it was handed (a documented <see cref="IGenWavePlugin.Register"/>
        /// contract violation — "must be inert") in a public static field, readable via reflection once
        /// the loader has moved on, so the TEST itself — not a race-prone background thread inside the
        /// plugin — can attempt the late <c>Add*</c> call directly and observe whether it throws.</summary>
        public static string RetainsHostForLateRegistration(string key) => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using GenWave.Core.Abstractions;
            using GenWave.Core.Domain;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public static IPluginHost? RetainedHost { get; private set; }

                public string Name => "Retains Host Test Plugin";

                public void Register(IPluginHost host)
                {
                    host.AddContextProvider(new Provider());
                    RetainedHost = host;
                }

                sealed class Provider : IContextProvider
                {
                    public string Key => "{{key}}";

                    public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
                }
            }
            """;

        /// <summary>T392 review finding 2f: a public class that never implements
        /// <c>IGenWavePlugin</c> at all — the manifest's <c>entryType</c> is pointed straight at it, so
        /// the loader's real type-check (never a string compare) is what rejects it.</summary>
        public static string NonPluginPublicClass() => """
            namespace TestPlugin;

            public sealed class NotAPlugin
            {
                public string Description => "A public class that never implements IGenWavePlugin.";
            }
            """;

        /// <summary>T392 review finding 2f: a valid <c>IGenWavePlugin</c> implementation with no
        /// public parameterless constructor — <c>Activator.CreateInstance</c> throws
        /// <c>MissingMethodException</c> for this shape, caught by the loader's own construction
        /// try/catch alongside any other construction failure.</summary>
        public static string ImplementsPluginButHasNoPublicParameterlessConstructor() => """
            using GenWave.Core.Abstractions;

            namespace TestPlugin;

            public sealed class EntryPoint : IGenWavePlugin
            {
                public EntryPoint(string requiredArgument) { }

                public string Name => "No Parameterless Ctor Test Plugin";

                public void Register(IPluginHost host) { }
            }
            """;
    }

    /// <summary>Writes one emitted plugin's full on-disk shape — <c>{root}/{slug}/plugin.json</c> plus
    /// its compiled assembly — composing <see cref="ManifestDocument"/> and
    /// <see cref="EmittedPluginAssembly"/> so a fact only ever states a slug and a source body.</summary>
    static class EmittedPlugin
    {
        public const string DefaultAssemblyFileName = "TestPlugin.dll";
        public const string DefaultEntryType = "TestPlugin.EntryPoint";

        public static string Create(
            string pluginsRoot, string slug, string sourceCode,
            string assemblyFileName = DefaultAssemblyFileName, string entryType = DefaultEntryType)
        {
            var directory = Directory.CreateDirectory(Path.Combine(pluginsRoot, slug)).FullName;
            var manifestJson = ManifestDocument.WithFields(new Dictionary<string, string?>
            {
                ["assembly"] = assemblyFileName,
                ["entryType"] = entryType,
            });

            File.WriteAllText(Path.Combine(directory, PluginManifestDiscovery.ManifestFileName), manifestJson);
            EmittedPluginAssembly.Emit(Path.Combine(directory, assemblyFileName), sourceCode);
            return directory;
        }

        /// <summary>The <see cref="AbstractionsTypesUnifyWithTheHost"/> fixture: a plugin whose OWN
        /// directory also carries its OWN copy of <c>GenWave.Abstractions.dll</c> beside its main
        /// assembly — the exact shape a real plugin's own build output could produce. T392 review
        /// finding 9: byte-identical to the host's own copy, not an actually differently-versioned
        /// (genuinely "stale") one — sufficient for what this fixture proves, since
        /// <see cref="PluginLoadContext.Load"/> refuses this assembly by NAME alone
        /// (<c>AbstractionsAssemblyName</c>), never by inspecting its version or content, so a real
        /// version mismatch would exercise no code path this refusal doesn't already cover.</summary>
        public static string CreateCarryingStaleAbstractions(string pluginsRoot, string slug, string sourceCode)
        {
            var directory = Create(pluginsRoot, slug, sourceCode);
            File.Copy(
                EmittedPluginAssembly.AbstractionsAssemblyPath,
                Path.Combine(directory, "GenWave.Abstractions.dll"),
                overwrite: true);
            return directory;
        }

        /// <summary>The <c>ACorruptDllSkipsTheWholePluginAndBootContinues</c> fixture: a manifest
        /// naming an <c>assembly</c> file that is not a valid .NET assembly at all.</summary>
        public static string CreateWithCorruptAssembly(string pluginsRoot, string slug)
        {
            var directory = Directory.CreateDirectory(Path.Combine(pluginsRoot, slug)).FullName;
            var manifestJson = ManifestDocument.WithField("assembly", DefaultAssemblyFileName);
            File.WriteAllText(Path.Combine(directory, PluginManifestDiscovery.ManifestFileName), manifestJson);
            EmittedPluginAssembly.WriteCorruptAssembly(Path.Combine(directory, DefaultAssemblyFileName));
            return directory;
        }
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
            // Given four plugin directories created in a deliberately NON-alphabetical order,
            // including a MIXED-CASE, UNDERSCORED slug ("Echo_Plugin") — T392 carry-forward A (T391
            // r2 review): an all-lowercase corpus can't mutation-discriminate StringComparer.Ordinal
            // from a culture-sensitive or case-INSENSITIVE comparer, since lowercase ASCII alone sorts
            // identically under all of them. Ordinal places uppercase 'E' (0x45) before every
            // lowercase leading letter here ('a'/'m'/'z', 0x61+), so "Echo_Plugin" sorts FIRST under
            // Ordinal — but would sort BETWEEN "alpha-plugin" and "mike-plugin" under
            // OrdinalIgnoreCase/CurrentCulture. A mutant swapping the comparer now fails visibly
            // instead of silently passing on an all-lowercase corpus.
            foreach (var slug in new[] { "zeta-plugin", "alpha-plugin", "mike-plugin", "Echo_Plugin" })
            {
                var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, slug));
                File.WriteAllText(Path.Combine(pluginDirectory.FullName, PluginManifestDiscovery.ManifestFileName), ManifestDocument.WellFormed());
            }

            // When discovery enumerates...
            var slugs = PluginManifestDiscovery.EnumerateCandidates(root).Select(c => c.Slug).ToList();

            // Then candidates yield in ascending StringComparer.Ordinal slug order (SPEC F156.6's
            // "earlier plugin" tiebreak) — NOT filesystem enumeration order, and NOT
            // culture-sensitive/case-insensitive order either (this corpus's own remarks above).
            Assert.Equal(new[] { "Echo_Plugin", "alpha-plugin", "mike-plugin", "zeta-plugin" }, slugs);
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

    public sealed class ScenarioAValidPluginLoadsInItsOwnContext : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-plugins-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void TheAssemblyLoadsInADedicatedLoadContext()
        {
            // Given two valid, DISTINCT emitted plugins...
            EmittedPlugin.Create(root, "alpha-plugin", PluginSource.SingleContextProvider("alpha-key"));
            EmittedPlugin.Create(root, "bravo-plugin", PluginSource.SingleContextProvider("bravo-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then both loaded...
            Assert.All(result.Reports, report => Assert.Equal(PluginLoadState.Loaded, report.State));
            Assert.Equal(2, result.ContextProviders.Count);

            // ...each in its OWN AssemblyLoadContext — never Default, and never shared with the
            // other plugin (SPEC F156.3, STORY-385 AC3).
            var loadContexts = result.ContextProviders
                .Select(provider => AssemblyLoadContext.GetLoadContext(provider.GetType().Assembly))
                .ToList();

            Assert.All(loadContexts, context => Assert.NotNull(context));
            Assert.All(loadContexts, context => Assert.NotSame(AssemblyLoadContext.Default, context));
            Assert.NotSame(loadContexts[0], loadContexts[1]);
        }

        [Fact]
        public void AbstractionsTypesUnifyWithTheHost()
        {
            // Given an emitted plugin that ALSO carries its OWN copy of GenWave.Abstractions.dll
            // beside its main assembly (byte-identical to the host's own — T392 review finding 9;
            // CreateCarryingStaleAbstractions's own remarks on why that suffices here)...
            EmittedPlugin.CreateCarryingStaleAbstractions(root, "carries-stale-abstractions", PluginSource.SingleContextProvider("stale-copy-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then it still loads: a broken unification would have made entryType's own
            // "IGenWavePlugin" a DIFFERENT CLR type than the host's, failing the loader's own
            // IsAssignableFrom check and skipping it as EntryTypeNotAPlugin instead of loading it.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Loaded, report.State);

            // ...and the committed provider's own IContextProvider interface resolves to the SAME
            // Assembly object as the host's (this test project's own) typeof(IContextProvider) —
            // direct proof the carried copy was ignored, not merely an inference from "it loaded".
            var provider = Assert.Single(result.ContextProviders);
            var resolvedContractAssembly = provider.GetType().GetInterface(nameof(IContextProvider))?.Assembly;
            Assert.Same(typeof(IContextProvider).Assembly, resolvedContractAssembly);
        }

        [Fact]
        public void RegisterRunsAndItsRegistrationsAreCollected()
        {
            // Given a valid emitted plugin whose Register adds exactly one IContextProvider...
            EmittedPlugin.Create(root, "sample-plugin", PluginSource.SingleContextProvider("sample-plugin-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the report says Loaded, naming the one contract added...
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Loaded, report.State);
            Assert.Equal(new[] { nameof(IContextProvider) }, report.Contracts);

            // ...and the collector holds exactly that one registration.
            var provider = Assert.Single(result.ContextProviders);
            Assert.Equal("sample-plugin-key", provider.Key);
        }

        [Fact]
        public void RegisteringAnAdSpotSourceCommitsItToTheResult()
        {
            // Given a valid emitted plugin whose Register adds exactly one IAdSpotSource — the OTHER
            // IPluginHost contract, never exercised by any fact above (T392 review finding 2b)...
            EmittedPlugin.Create(root, "ad-spot-plugin", PluginSource.SingleAdSpotSource());

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the report says Loaded, naming the one contract added...
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Loaded, report.State);
            Assert.Equal(new[] { nameof(IAdSpotSource) }, report.Contracts);

            // ...and the collector holds exactly that one registration — never folded into
            // ContextProviders.
            Assert.Single(result.AdSpotSources);
            Assert.Empty(result.ContextProviders);
        }
    }

    public sealed class ScenarioRegistrationBufferSealing : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-plugins-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        sealed class LateProvider : IContextProvider
        {
            public string Key => "late-provider-key";

            public Task<ContextContent?> FetchAsync(CancellationToken ct) => Task.FromResult<ContextContent?>(null);
        }

        [Fact]
        public void ARetainedHostThrowsOnALateAddAfterRegisterReturnsAndNeverAltersTheCommittedReport()
        {
            // Given a plugin that registers one provider DURING Register, then retains the
            // IPluginHost it was handed (T392 review finding 3 — the buffer-sealing/aliasing pin)...
            EmittedPlugin.Create(root, "retains-host-plugin", PluginSource.RetainsHostForLateRegistration("retained-host-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then it committed cleanly, exactly the one registration made DURING Register...
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Loaded, report.State);
            Assert.Equal(new[] { nameof(IContextProvider) }, report.Contracts);
            var provider = Assert.Single(result.ContextProviders);
            Assert.Equal("retained-host-key", provider.Key);

            // ...and reflecting into the plugin's own ALC to reach the host it retained (safe:
            // IPluginHost itself unifies to the host's own type, the same mechanism
            // AbstractionsTypesUnifyWithTheHost proves) — a LATE Add* call, attempted directly from
            // this test rather than a race-prone background thread inside the plugin, after Register
            // already returned and the loader already sealed the buffer...
            var entryType = provider.GetType().Assembly.GetType("TestPlugin.EntryPoint")
                ?? throw new InvalidOperationException("TestPlugin.EntryPoint was not found in the emitted assembly.");
            var retainedHostProperty = entryType.GetProperty("RetainedHost", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("TestPlugin.EntryPoint.RetainedHost was not found via reflection.");
            var retainedHost = (IPluginHost?)retainedHostProperty.GetValue(null);
            Assert.NotNull(retainedHost);

            var lateCallException = Record.Exception(() => retainedHost!.AddContextProvider(new LateProvider()));

            // ...throws, never silently accepted...
            Assert.IsType<InvalidOperationException>(lateCallException);

            // ...and the report/result the loader already returned are UNCHANGED — no forged,
            // post-commit second registration snuck into either.
            Assert.Single(result.ContextProviders);
            Assert.Equal(new[] { nameof(IContextProvider) }, report.Contracts);
        }
    }

    public sealed class ScenarioAbstractionsNameRefusalIsCaseInsensitive
    {
        [Theory]
        [InlineData("GenWave.Abstractions")]
        [InlineData("genwave.abstractions")]
        [InlineData("GENWAVE.ABSTRACTIONS")]
        [InlineData("GenWave.ABSTRACTIONS")]
        public void LoadRefusesTheAbstractionsNameRegardlessOfCasing(string requestedName)
        {
            // Given a PluginLoadContext (T392 review advisory 2 — OrdinalIgnoreCase, finding 4's own
            // fix) — anchored on ANY real, existing assembly path (the resolver only needs a valid
            // file to construct against; this test never resolves anything through it, since the
            // Abstractions-name refusal short-circuits before the resolver is ever consulted)...
            //
            // A differently-cased AssemblyName.Name can never arise organically through a normal
            // compile-and-load (the CLR always requests the exact case embedded in the REFERENCED
            // assembly's own manifest, regardless of how source code spells a using directive), so
            // this pins PluginLoadContext's own Load override directly, via reflection — the one
            // seam an emitted-plugin fixture cannot reach.
            var loadContext = new PluginLoadContext(typeof(PluginLoader).Assembly.Location);
            var loadMethod = typeof(PluginLoadContext).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("PluginLoadContext.Load was not found via reflection.");

            // When Load is asked to resolve that name, in whatever casing...
            var resolved = loadMethod.Invoke(loadContext, new object[] { new AssemblyName(requestedName) });

            // Then it refuses (null) — the same fallback path that lets the CLR resolve it against
            // AssemblyLoadContext.Default instead, regardless of the requested name's casing.
            Assert.Null(resolved);
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

    public sealed class ScenarioSkippingBrokenPlugins : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("genwave-plugins-").FullName;

        public void Dispose() => Directory.Delete(root, recursive: true);

        [Fact]
        public void ACorruptDllSkipsTheWholePluginAndBootContinues()
        {
            // Given a plugins dir containing a corrupt "assembly" (garbage bytes, not a real .NET
            // DLL) alongside a perfectly valid plugin...
            EmittedPlugin.CreateWithCorruptAssembly(root, "corrupt-plugin");
            EmittedPlugin.Create(root, "valid-plugin", PluginSource.SingleContextProvider("valid-plugin-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then zero registrations came from the corrupt directory, one report names the cause...
            var corruptReport = result.Reports.Single(r => r.Slug == "corrupt-plugin");
            Assert.Equal(PluginLoadState.Skipped, corruptReport.State);
            Assert.Equal(PluginLoadFailureReason.AssemblyLoadFailed, corruptReport.Reason);

            // ...and the valid plugin still loaded — boot continues (SPEC F156.4).
            var validReport = result.Reports.Single(r => r.Slug == "valid-plugin");
            Assert.Equal(PluginLoadState.Loaded, validReport.State);
            Assert.Single(result.ContextProviders);
        }

        [Fact]
        public void AThrowingRegisterLeavesNoPartialRegistrations()
        {
            // Given a plugin whose Register adds one provider, then throws...
            EmittedPlugin.Create(root, "throwing-plugin", PluginSource.ContextProviderThenThrowingRegister("throwing-plugin-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the plugin is skipped whole, naming RegisterThrew, and the collector holds
            // NOTHING from it — the buffer commits only after Register RETURNS (STORY-385 AC8).
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.RegisterThrew, report.Reason);
            Assert.Empty(result.ContextProviders);
        }

        [Fact]
        public void AContextKeyCollisionSkipsTheColliderWhole()
        {
            // Given an emitted plugin whose IContextProvider.Key equals "weather" — a built-in key...
            EmittedPlugin.Create(root, "collides-with-builtin", PluginSource.SingleContextProvider("weather"));

            // When the loader pre-validates against the built-in key set...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string> { "weather" });

            // Then that plugin is skipped whole, naming the key — pre-validation catches it here,
            // never by way of a real ContextPipeline constructor discovering the collision (F156.6).
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.ContextProviderKeyCollision, report.Reason);
            Assert.Contains("weather", report.Detail);
            Assert.Empty(result.ContextProviders);
        }

        [Fact]
        public void TwoPluginsCollidingOnAKeyLoadFirstSkipSecond()
        {
            // Given two plugins, deliberately named so "alpha-plugin" sorts BEFORE "bravo-plugin"
            // (StringComparer.Ordinal, F156.6's own tiebreak), both registering the SAME
            // (otherwise-valid, non-built-in) key...
            EmittedPlugin.Create(root, "alpha-plugin", PluginSource.SingleContextProvider("shared-key"));
            EmittedPlugin.Create(root, "bravo-plugin", PluginSource.SingleContextProvider("shared-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the EARLIER plugin (alpha) loads and the LATER one (bravo) is skipped as a
            // collision — never the reverse.
            var alphaReport = result.Reports.Single(r => r.Slug == "alpha-plugin");
            var bravoReport = result.Reports.Single(r => r.Slug == "bravo-plugin");

            Assert.Equal(PluginLoadState.Loaded, alphaReport.State);
            Assert.Equal(PluginLoadState.Skipped, bravoReport.State);
            Assert.Equal(PluginLoadFailureReason.ContextProviderKeyCollision, bravoReport.Reason);

            var provider = Assert.Single(result.ContextProviders);
            Assert.Equal("shared-key", provider.Key);
        }

        [Fact]
        public void AnInvalidContextProviderKeySkipsTheWholePlugin()
        {
            // Given an emitted plugin whose IContextProvider.Key carries uppercase letters and a
            // space — structurally invalid under IContextProvider.Key's own
            // lowercase-ASCII/digit/hyphen contract, not merely colliding with anything.
            EmittedPlugin.Create(root, "invalid-key-plugin", PluginSource.SingleContextProvider("Invalid Key!"));

            // When the loader pre-validates...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, naming the format violation.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.ContextProviderKeyInvalid, report.Reason);
            Assert.Empty(result.ContextProviders);
        }

        [Fact]
        public void ASymlinkedManifestFileSkipsTheWholePlugin()
        {
            // Given a real, non-symlinked plugin DIRECTORY (so PluginManifestDiscovery's own
            // directory-level check still yields a candidate) whose plugin.json is ITSELF a symlink
            // to a well-formed manifest sitting outside it — T392 carry-forward D: the manifest FILE,
            // not just the directory.
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "symlinked-manifest-plugin")).FullName;
            var realManifestPath = Path.Combine(root, "real-manifest.json");
            File.WriteAllText(realManifestPath, ManifestDocument.WellFormed());
            File.CreateSymbolicLink(Path.Combine(pluginDirectory, PluginManifestDiscovery.ManifestFileName), realManifestPath);

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, refusing to read the symlinked manifest at all.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.ManifestUnreadable, report.Reason);
            Assert.Contains("symlink", report.Detail);
        }

        [Fact]
        public void ASymlinkedAssemblyFileSkipsTheWholePlugin()
        {
            // Given a real, well-formed manifest whose 'assembly' file is ITSELF a symlink to a
            // real, valid assembly sitting outside the plugin directory — T392 carry-forward D's
            // second half.
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "symlinked-assembly-plugin")).FullName;
            File.WriteAllText(
                Path.Combine(pluginDirectory, PluginManifestDiscovery.ManifestFileName),
                ManifestDocument.WithFields(new Dictionary<string, string?>
                {
                    ["assembly"] = EmittedPlugin.DefaultAssemblyFileName,
                    ["entryType"] = EmittedPlugin.DefaultEntryType,
                }));

            var realAssemblyPath = Path.Combine(root, "real-assembly.dll");
            EmittedPluginAssembly.Emit(realAssemblyPath, PluginSource.SingleContextProvider("symlinked-assembly-key"));
            File.CreateSymbolicLink(Path.Combine(pluginDirectory, EmittedPlugin.DefaultAssemblyFileName), realAssemblyPath);

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, refusing to load the symlinked assembly at all.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.AssemblyFileInvalid, report.Reason);
            Assert.Contains("symlink", report.Detail);
        }

        [Fact]
        public void TwoProvidersInOnePluginSharingAKeySkipsThePluginWhole()
        {
            // Given ONE plugin whose Register buffers TWO providers sharing the same key — T392
            // review finding 2c, the within-plugin collision branch, distinct from the cross-plugin
            // collision facts above.
            EmittedPlugin.Create(root, "self-colliding-plugin", PluginSource.TwoContextProvidersSharingAKey("dup-key"));

            // When the loader pre-validates...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, naming the collision — never one provider committed
            // and the other rejected.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.ContextProviderKeyCollision, report.Reason);
            Assert.Empty(result.ContextProviders);
        }

        [Fact]
        public void ARegisterExceptionMessageContainingCrLfProducesASingleLineDetail()
        {
            // Given a plugin whose Register throws with a raw CR/LF payload IN THE EXCEPTION'S OWN
            // MESSAGE (T392 review finding 2a) — distinct from ScenarioRejectingBadManifests's own
            // CR/LF facts, which craft the payload into a MANIFEST field, never an exception message.
            EmittedPlugin.Create(root, "crlf-throwing-plugin", PluginSource.RegisterThrowsWithCrLfMessage());

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then it still refuses, naming RegisterThrew...
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.RegisterThrew, report.Reason);

            // ...but Detail is neutralized to a single line — CWE-117 log forging closed even when
            // the crafted payload arrives via a thrown exception's own Message.
            Assert.DoesNotContain('\r', report.Detail);
            Assert.DoesNotContain('\n', report.Detail);
        }

        [Fact]
        public void AMissingAssemblyFileIsItsOwnTypedReason()
        {
            // Given a well-formed manifest naming an 'assembly' file that was never actually shipped
            // beside it (T392 review finding 2d) — its own typed reason, never folded into
            // AssemblyLoadFailed (a DIFFERENT, later failure: a file that exists but won't load).
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "missing-assembly-plugin")).FullName;
            File.WriteAllText(
                Path.Combine(pluginDirectory, PluginManifestDiscovery.ManifestFileName),
                ManifestDocument.WithField("assembly", "DoesNotExist.dll"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, naming AssemblyFileMissing specifically.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.AssemblyFileMissing, report.Reason);
        }

        [Fact]
        public void AnOversizedManifestIsSkippedAsUnreadable()
        {
            // Given a manifest document past the loader's own 64 KiB bounded-read ceiling (T392
            // review finding 2e — PluginLoader.ManifestMaxBytes; a well-formed document padded past
            // the bound with an oversized 'name' value)...
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "oversized-manifest-plugin")).FullName;
            var oversizedName = new string('x', 70 * 1024);
            File.WriteAllText(
                Path.Combine(pluginDirectory, PluginManifestDiscovery.ManifestFileName),
                ManifestDocument.WithField("name", oversizedName));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped as unreadable — the bound rejects it before the
            // document is ever handed to the parser.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.ManifestUnreadable, report.Reason);
        }

        [Fact]
        public void AnEntryTypeNamingNoTypeInTheAssemblySkipsAsNotFound()
        {
            // Given a valid emitted assembly whose manifest 'entryType' names a type that assembly
            // does not actually carry (T392 review finding 2f, EntryTypeNotFound)...
            EmittedPlugin.Create(
                root, "wrong-entrytype-plugin", PluginSource.SingleContextProvider("wrong-entrytype-key"),
                entryType: "TestPlugin.NoSuchType");

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, naming EntryTypeNotFound.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.EntryTypeNotFound, report.Reason);
        }

        [Fact]
        public void AnEntryTypeNotImplementingIGenWavePluginSkipsAsNotAPlugin()
        {
            // Given a real, public, constructible class that never implements IGenWavePlugin at all
            // (T392 review finding 2f, EntryTypeNotAPlugin)...
            EmittedPlugin.Create(
                root, "non-plugin-entrytype-plugin", PluginSource.NonPluginPublicClass(),
                entryType: "TestPlugin.NotAPlugin");

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, naming EntryTypeNotAPlugin.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.EntryTypeNotAPlugin, report.Reason);
        }

        [Fact]
        public void AnEntryTypeWithNoPublicParameterlessConstructorSkipsAsNotConstructible()
        {
            // Given a valid IGenWavePlugin implementation whose only constructor takes a required
            // argument (T392 review finding 2f, EntryTypeNotConstructible)...
            EmittedPlugin.Create(root, "no-ctor-plugin", PluginSource.ImplementsPluginButHasNoPublicParameterlessConstructor());

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped, naming EntryTypeNotConstructible.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.EntryTypeNotConstructible, report.Reason);
        }

        [Fact]
        public void AManifestInvalidAtParseTimeIsReportedByTheLoaderNotJustTheParser()
        {
            // Given a plugin directory whose manifest is malformed (missing 'entryType') — T392
            // review's "also pin ManifestInvalid THROUGH the loader": ScenarioRejectingBadManifests
            // above only ever exercises PluginManifestParser directly, never LoadAll.
            var pluginDirectory = Directory.CreateDirectory(Path.Combine(root, "invalid-manifest-plugin")).FullName;
            File.WriteAllText(
                Path.Combine(pluginDirectory, PluginManifestDiscovery.ManifestFileName),
                ManifestDocument.Missing("entryType"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then LoadAll itself reports the rejection, naming the field.
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.ManifestInvalid, report.Reason);
            Assert.Contains(nameof(PluginManifestField.EntryType), report.Detail);
        }

        [Fact]
        public void AProviderWhoseKeyGetterThrowsIsSkippedAsUnexpectedAndLoadAllReturnsNormally()
        {
            // Given an emitted plugin whose sole IContextProvider throws from its OWN Key getter
            // (T392 round-2 review finding B1) — a member this loader itself reads during
            // pre-validation, not merely FetchAsync...
            EmittedPlugin.Create(root, "throwing-key-plugin", PluginSource.ContextProviderWithThrowingKeyGetter());

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the whole plugin is skipped as Unexpected — the outer safety net converts the
            // unanticipated exception into a typed report, rather than letting it escape LoadAll
            // (a station-down shape at T394's Program.cs call site under the mutant that replaces
            // this catch's body with a bare rethrow).
            var report = Assert.Single(result.Reports);
            Assert.Equal(PluginLoadState.Skipped, report.State);
            Assert.Equal(PluginLoadFailureReason.Unexpected, report.Reason);
            Assert.Empty(result.ContextProviders);
        }

        [Fact]
        public void ADriftingKeyGetterCommitsTheFirstReadKeyNeverASecondLiveRead()
        {
            // Given a provider whose Key getter answers "safe-key" on its FIRST call and
            // "unrelated-key" on every call after (T392 round-2 review finding B2's own drift shape),
            // followed by a SECOND, ordinarily-loading plugin that ALSO registers "safe-key" —
            // ascending slug order ('d' < 's') guarantees the drifting plugin commits FIRST...
            EmittedPlugin.Create(root, "drifting-key-plugin", PluginSource.ContextProviderWithDriftingKey("safe-key", "unrelated-key"));
            EmittedPlugin.Create(root, "second-plugin", PluginSource.SingleContextProvider("safe-key"));

            // When the loader runs...
            var loader = new PluginLoader(settingKey => null);
            var result = loader.LoadAll(root, new HashSet<string>());

            // Then the drifting plugin loaded, committing its FIRST-read key — proven not by
            // re-reading its own (drifting) Key property again from this test, but by the SECOND
            // plugin's own outcome: had the loader instead committed a re-read, SECOND value into the
            // running key set (the B2 mutant), "safe-key" would never have been reserved and the
            // second plugin would ALSO have loaded — it does not.
            var driftingReport = result.Reports.Single(r => r.Slug == "drifting-key-plugin");
            var secondReport = result.Reports.Single(r => r.Slug == "second-plugin");

            Assert.Equal(PluginLoadState.Loaded, driftingReport.State);
            Assert.Equal(PluginLoadState.Skipped, secondReport.State);
            Assert.Equal(PluginLoadFailureReason.ContextProviderKeyCollision, secondReport.Reason);
            Assert.Contains("safe-key", secondReport.Detail);
        }
    }

    public sealed class ScenarioRootUnreadable
    {
        [Fact]
        public void AnUnreadableRootYieldsARootUnreadableReportInsteadOfThrowing()
        {
            // Given a real, existing plugins root, stripped of every permission bit (T392 review
            // finding 1 — chmod 000, the exact shape empirically proven to raise
            // UnauthorizedAccessException out of Directory.EnumerateDirectories)...
            var root = Directory.CreateTempSubdirectory("genwave-plugins-root-unreadable-").FullName;
            try
            {
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(root, UnixFileMode.None);

                // When the loader runs...
                var loader = new PluginLoader(settingKey => null);
                var result = loader.LoadAll(root, new HashSet<string>());

                // Then, above all: LoadAll returns NORMALLY either way — the one invariant that
                // survives privilege level, since a privileged (root) test-runner process bypasses
                // discretionary permission checks entirely (CAP_DAC_OVERRIDE) and would list a
                // chmod-000 directory just fine, empirically confirmed never to throw here regardless.
                if (Environment.IsPrivilegedProcess || !OperatingSystem.IsLinux())
                {
                    // The chmod denied nothing (or never applied) — this degrades to the ordinary
                    // "empty plugins root" shape. Still exercises the one thing that matters under
                    // any privilege level: LoadAll never throws.
                    Assert.Empty(result.Reports);
                }
                else
                {
                    // Then the loader reports ONE RootUnreadable outcome, naming the root path — never
                    // an unhandled UnauthorizedAccessException out of LoadAll.
                    var report = Assert.Single(result.Reports);
                    Assert.Equal(PluginLoadState.RootUnreadable, report.State);
                    Assert.Equal(PluginLoadFailureReason.RootUnreadable, report.Reason);
                    Assert.Equal(string.Empty, report.Slug);
                    Assert.Contains(root, report.Detail);
                }
            }
            finally
            {
                if (OperatingSystem.IsLinux())
                {
                    File.SetUnixFileMode(
                        root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                Directory.Delete(root, recursive: true);
            }
        }
    }
}
