namespace GenWave.Host.Tests.Support;

using System.Collections.Immutable;
using GenWave.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Compiles a throwaway <c>IGenWavePlugin</c> assembly, at test time, and writes it out as a full
/// <c>{root}/{slug}/</c> plugin payload — this assembly's own counterpart to
/// <c>GenWave.Plugins.Tests.Support.EmittedPluginAssembly</c>/<c>EmittedPlugin</c> (T394 review
/// HIGH-2's own drifting-key-through-composition fact and the null/blank-key pin both need a plugin
/// whose <c>Register</c> body does something the shipped example plugin never does, exercised through
/// the REAL <c>WebApplicationFactory&lt;Program&gt;</c> composition — not
/// <c>GenWave.Plugins.Tests</c>' own in-process <c>PluginLoader.LoadAll</c> call, which already
/// covers the loader's own half of both properties). Deliberately its own small compiler, not a
/// cross-assembly reference to <c>GenWave.Plugins.Tests.Support.EmittedPluginAssembly</c> — that
/// project's own remarks (and <c>ExamplePluginPayload</c>'s, one file over) reject exactly that
/// coupling for a walk-up a fraction of its size; this compiler is smaller still (no corrupt-assembly
/// writer, no multi-field manifest builder — Story386's own facts need neither).
/// </summary>
internal static class EmittedHostTestPlugin
{
    const string TargetFrameworkMoniker = "net10.0";
    const string AssemblyFileName = "Plugin.dll";

    static readonly Lazy<ImmutableArray<MetadataReference>> ReferenceAssemblies = new(ResolveReferenceAssemblies);

    /// <summary>Writes <c>{pluginsRoot}/{slug}/plugin.json</c> (naming <paramref name="entryTypeFullName"/>)
    /// plus <paramref name="sourceCode"/> compiled to <c>{pluginsRoot}/{slug}/Plugin.dll</c> — a
    /// complete, loadable plugin payload.</summary>
    public static void CreateInto(string pluginsRoot, string slug, string manifestName, string entryTypeFullName, string sourceCode)
    {
        var directory = Directory.CreateDirectory(Path.Combine(pluginsRoot, slug)).FullName;

        var manifestJson = $$"""
            {
              "name": "{{manifestName}}",
              "version": "1.0.0",
              "assembly": "{{AssemblyFileName}}",
              "entryType": "{{entryTypeFullName}}",
              "abstractions": "5.6.0"
            }
            """;

        File.WriteAllText(Path.Combine(directory, "plugin.json"), manifestJson);
        Emit(Path.Combine(directory, AssemblyFileName), sourceCode);
    }

    static void Emit(string outputDllPath, string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(outputDllPath),
            [syntaxTree],
            ReferenceAssemblies.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(outputDllPath);
        if (!result.Success)
        {
            var diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString()));
            throw new InvalidOperationException($"Emitting the test plugin \"{outputDllPath}\" failed:{Environment.NewLine}{diagnostics}");
        }
    }

    static ImmutableArray<MetadataReference> ResolveReferenceAssemblies()
    {
        var bcl = Directory.EnumerateFiles(ResolveNetCoreRefDirectory(), "*.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var abstractions = MetadataReference.CreateFromFile(typeof(IGenWavePlugin).Assembly.Location);

        return bcl.Append(abstractions).ToImmutableArray();
    }

    /// <summary>Mirrors <c>GenWave.Plugins.Tests.Support.EmittedPluginAssembly</c>'s own identical
    /// walk-up (that class's own remarks explain the version-descending ordering and why no hardcoded
    /// SDK path is used) — duplicated here rather than referenced, for the same cross-assembly-coupling
    /// reason this whole type exists standalone.</summary>
    static string ResolveNetCoreRefDirectory()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve the running .NET runtime's own directory.");

        var dotnetRoot = Directory.GetParent(runtimeDirectory)?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException($"Could not resolve the dotnet install root from \"{runtimeDirectory}\".");

        var refPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var netTfmRefDirectory = Directory.EnumerateDirectories(refPackRoot)
            .Select(versionDirectory => (
                RefDirectory: Path.Combine(versionDirectory, "ref", TargetFrameworkMoniker),
                Version: ParsePackVersion(versionDirectory)))
            .Where(candidate => Directory.Exists(candidate.RefDirectory))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.RefDirectory)
            .FirstOrDefault();

        return netTfmRefDirectory
            ?? throw new InvalidOperationException($"No {TargetFrameworkMoniker} reference assemblies found under \"{refPackRoot}\".");
    }

    static Version ParsePackVersion(string versionDirectory)
    {
        var versionText = Path.GetFileName(versionDirectory);
        var numericCore = versionText.Split('-', 2)[0];

        return Version.TryParse(numericCore, out var version) ? version : new Version(0, 0, 0, 0);
    }
}
