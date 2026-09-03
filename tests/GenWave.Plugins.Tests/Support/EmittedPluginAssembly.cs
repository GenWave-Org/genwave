namespace GenWave.Plugins.Tests.Support;

using System.Collections.Immutable;
using GenWave.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// Compiles a throwaway <c>IGenWavePlugin</c> assembly, at test time, straight to a <c>.dll</c> on
/// disk — CI stays hermetic (no fixture project ships in-repo, per PLAN T392's own line) while still
/// exercising the REAL <see cref="System.Runtime.Loader.AssemblyLoadContext"/>/entryType-activation
/// path <see cref="PluginLoader"/> runs in production, against a REAL, valid (or deliberately corrupt)
/// .NET assembly. The genwave-plugin-example repo (PLAN T393/T394) is the actual third-party proof;
/// this is the loader's own hermetic one.
/// </summary>
internal static class EmittedPluginAssembly
{
    const string TargetFrameworkMoniker = "net10.0";

    static readonly Lazy<ImmutableArray<MetadataReference>> ReferenceAssemblies = new(ResolveReferenceAssemblies);

    /// <summary>
    /// Compiles <paramref name="sourceCode"/> (one C# source file — everything an emitted test plugin
    /// needs fits in one) into <paramref name="outputDllPath"/>. Throws
    /// <see cref="InvalidOperationException"/>, naming every diagnostic, on any compile error — a spec
    /// whose own fixture source doesn't compile is a broken spec, never a "corrupt DLL" fact (those
    /// write raw garbage bytes instead; see <see cref="WriteCorruptAssembly"/>).
    /// </summary>
    public static void Emit(string outputDllPath, string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(outputDllPath),
            new[] { syntaxTree },
            ReferenceAssemblies.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(outputDllPath);
        if (!result.Success)
        {
            var diagnostics = string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString()));
            throw new InvalidOperationException($"Emitting test plugin \"{outputDllPath}\" failed:{Environment.NewLine}{diagnostics}");
        }
    }

    /// <summary>Writes bytes that are not a valid .NET assembly at all — the SPEC F156.4 "corrupt DLL"
    /// case, distinct from a well-formed assembly that merely fails a later loader check.</summary>
    public static void WriteCorruptAssembly(string outputDllPath) =>
        File.WriteAllBytes(outputDllPath, "this is not a .NET assembly, just garbage bytes"u8.ToArray());

    /// <summary>The real, currently-loaded <c>GenWave.Abstractions.dll</c> this test process itself
    /// runs against — resolved via reflection (<see cref="IGenWavePlugin"/>'s own assembly), never a
    /// hardcoded path, so it tracks whatever build configuration actually ran the test.</summary>
    public static string AbstractionsAssemblyPath => typeof(IGenWavePlugin).Assembly.Location;

    static ImmutableArray<MetadataReference> ResolveReferenceAssemblies()
    {
        var bcl = Directory.EnumerateFiles(ResolveNetCoreRefDirectory(), "*.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var abstractions = MetadataReference.CreateFromFile(AbstractionsAssemblyPath);

        return bcl.Append(abstractions).ToImmutableArray();
    }

    /// <summary>
    /// Locates the installed SDK's <c>net10.0</c> reference-assembly directory
    /// (<c>packs/Microsoft.NETCore.App.Ref/{version}/ref/net10.0</c>) by walking up from the currently
    /// running runtime's own directory — no hardcoded SDK install path, so this resolves correctly on
    /// any box (or CI image) that can run this test at all, regardless of exactly which patch version
    /// of the ref pack happens to be installed alongside it. A box can carry MORE THAN ONE ref pack
    /// version side by side (e.g. an in-place SDK upgrade that left an older pack on disk, or multiple
    /// SDK feature bands installed together) — ordered by version DESCENDING (T392 review finding 8),
    /// not <c>FirstOrDefault</c> over unspecified filesystem enumeration order, so this always resolves
    /// the newest ref pack actually installed, matching what a real `dotnet build` on this same box
    /// would pick.
    /// </summary>
    static string ResolveNetCoreRefDirectory()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve the running .NET runtime's own directory.");

        // .../shared/Microsoft.NETCore.App/{runtimeVersion}/System.Private.CoreLib.dll -> the dotnet
        // installation root sits three levels above the runtime version directory.
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

    /// <summary>Parses a ref-pack version DIRECTORY's own name (e.g. <c>"10.0.0"</c>, or a pre-release
    /// build's <c>"10.0.0-rc.1.24451.1"</c>) down to its numeric <see cref="Version"/> core — only the
    /// dotted-numeric prefix before any <c>-</c> pre-release suffix, since <see cref="Version"/> itself
    /// cannot parse one. A directory name that fails to parse at all sorts as <c>0.0.0.0</c> — last, by
    /// construction — rather than throwing, since a stray non-version directory here is a filesystem
    /// oddity the resolver's own <c>Directory.Exists</c> filter above already tolerates.</summary>
    static Version ParsePackVersion(string versionDirectory)
    {
        var versionText = Path.GetFileName(versionDirectory);
        var numericCore = versionText.Split('-', 2)[0];

        return Version.TryParse(numericCore, out var version) ? version : new Version(0, 0, 0, 0);
    }
}
