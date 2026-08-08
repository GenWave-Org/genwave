using System.Text.Json;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L4-references' detector: reads a project's own build-output <c>.deps.json</c> "libraries" map
/// instead of matching assembly names against a BCL prefix (<c>StartsWith("System")</c>). A
/// name-prefix check has a proven bypass: a real <c>PackageReference</c> to a <c>System.*</c>-named
/// third-party package (e.g. <c>System.Diagnostics.EventLog</c>) never fails the name check yet
/// ships straight into the published package's dependency closure — and the same shape of bug (a
/// prefix match without a segment boundary) would also mis-match an unrelated assembly named e.g.
/// <c>SystemFoo</c>. The libraries map has neither hole: every restored NuGet/project dependency
/// gets exactly one entry there, and shared-framework (BCL) assemblies never do — they're resolved
/// from the installed runtime, not restored as a library — so "nothing beyond the BCL" is exactly
/// "no library entry besides the project's own self-entry", with no name matching involved at all.
///
/// This reads the deps.json a project's OWN build produces in ITS OWN output directory — not the
/// same-named file the SDK copies alongside a project reference's DLL into a *consuming* project's
/// output. That copy is a bare self-entry stub with no dependency information regardless of what
/// the referenced project actually depends on (verified at T211 review: adding
/// <c>PackageReference System.Diagnostics.EventLog</c> to GenWave.Abstractions changed
/// Abstractions' own build-output deps.json but left the copy inside this test project's output
/// directory untouched).
/// </summary>
internal static class DepsJsonDependencyScan
{
    /// <summary>Every entry in <paramref name="depsJsonContent"/>'s "libraries" map whose name (the
    /// part before the "/version" suffix) is not <paramref name="selfAssemblyName"/> — i.e. every
    /// dependency beyond the project itself. Operates on raw JSON text rather than a file path, so a
    /// probe can hand it synthetic content with no build or disk involved.</summary>
    public static IReadOnlyList<string> ExtraLibraries(string depsJsonContent, string selfAssemblyName)
    {
        using var document = JsonDocument.Parse(depsJsonContent);
        var libraries = document.RootElement.GetProperty("libraries");

        var extra = new List<string>();
        foreach (var library in libraries.EnumerateObject())
        {
            var separator = library.Name.IndexOf('/');
            var name = separator < 0 ? library.Name : library.Name[..separator];
            if (name != selfAssemblyName)
                extra.Add(library.Name);
        }

        return extra;
    }

    /// <summary>Reads <paramref name="assemblyName"/>'s own build-output deps.json — found under
    /// <paramref name="projectDirectoryRelativeToSolution"/>'s <c>bin/&lt;Configuration&gt;/
    /// &lt;TargetFramework&gt;/</c>, with Configuration and TargetFramework read off THIS test run's
    /// own output directory rather than hardcoded — and returns its extra library entries. Robust to
    /// whether `dotnet test` targeted just this project or the whole solution, and to Debug/Release,
    /// because every project in the graph builds under the same configuration and (here) the same
    /// target framework.</summary>
    public static IReadOnlyList<string> ExtraLibrariesForProject(
        string projectDirectoryRelativeToSolution, string assemblyName)
    {
        var depsJsonPath = ResolveDepsJsonPath(projectDirectoryRelativeToSolution, assemblyName);

        if (!File.Exists(depsJsonPath))
        {
            throw new FileNotFoundException(
                $"No build-output deps.json for \"{assemblyName}\" at \"{depsJsonPath}\" — expected " +
                "the project to have built as part of this test run's own dependency graph.",
                depsJsonPath);
        }

        return ExtraLibraries(File.ReadAllText(depsJsonPath), assemblyName);
    }

    private static string ResolveDepsJsonPath(string projectDirectoryRelativeToSolution, string assemblyName)
    {
        var solutionRoot = SolutionLocator.Root();

        var targetFrameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = targetFrameworkDirectory.Parent
            ?? throw new InvalidOperationException(
                $"\"{AppContext.BaseDirectory}\" has no Configuration directory above its TargetFramework one.");

        return Path.Combine(
            solutionRoot,
            projectDirectoryRelativeToSolution,
            "bin",
            configurationDirectory.Name,
            targetFrameworkDirectory.Name,
            $"{assemblyName}.deps.json");
    }
}
