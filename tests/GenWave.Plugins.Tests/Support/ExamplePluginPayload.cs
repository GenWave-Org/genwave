namespace GenWave.Plugins.Tests.Support;

/// <summary>
/// Locates <c>examples/genwave-plugin-example</c>'s own <c>dotnet build</c> output — the plugin SPI's
/// REAL, third-party-style reference consumer (PLAN T393, STORY-386) — and copies it into a fresh
/// directory shaped exactly like <c>{Plugins:Root}/&lt;slug&gt;/</c>, the same shape a real operator's
/// <c>compose.plugins.yaml</c> mount produces (PLAN T394). <see cref="PluginLoader"/> can then be
/// pointed at that directory exactly the way it is pointed at a real mount.
///
/// <para>
/// <b>The trick that keeps this hermetic without loading the example into THIS process</b>
/// (<c>GenWave.Plugins.Tests.csproj</c>'s own comment on the same subject): the example project is a
/// <c>ProjectReference</c> on this test project with <c>ReferenceOutputAssembly="false"</c> — MSBuild
/// still builds it, fresh, as part of THIS project's own build order (so its output below always
/// exists by the time a test runs), but never adds its assembly to this project's own compile
/// references or copies it into this project's own output directory. This class only ever reads that
/// build output as PLAIN FILES ON DISK and copies them — the assembly is loaded for real exactly once,
/// by <see cref="PluginLoader"/>, into its own dedicated <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// (SPEC F156.3) — never into this test process's own <c>AssemblyLoadContext.Default</c>, which a
/// direct assembly reference here would have done instead, silently defeating the very isolation this
/// suite exists to prove.
/// </para>
///
/// <para>
/// <b>Its own repo-root walk-up, deliberately not <c>GenWave.SeamIndexGenerator.RepoRoot.Find</c>.</b>
/// <c>GenWave.Architecture.Tests.Support.SolutionLocator</c> already has a repo-root finder, but it
/// delegates to that CLI TOOL project (<c>tools/SeamIndexGenerator</c>) via a <c>ProjectReference</c>
/// Architecture.Tests already carries for other reasons (its SEAMS-index facts). Pulling in the same
/// tool project here, just for this one walk-up, would hand a test project a real dependency on a
/// CLI tool's own project graph for a five-line loop — worse coupling than the few duplicated lines
/// <see cref="FindRepoRoot"/> costs. Kept deliberately small and local instead.
/// </para>
/// </summary>
internal static class ExamplePluginPayload
{
    const string ExampleProjectRelativePath = "examples/genwave-plugin-example";
    const string ExampleAssemblyFileName = "ExamplePlugin.dll";

    /// <summary>
    /// Copies every file from the example project's own build output — its assembly, symbols, and the
    /// <c>plugin.json</c> its own <c>.csproj</c> copies alongside them (that project's own
    /// <c>CopyToOutputDirectory</c> comment) — into <c>"{pluginsRoot}/{slug}/"</c>, creating both
    /// directories as needed.
    /// </summary>
    public static void CopyInto(string pluginsRoot, string slug)
    {
        var sourceDirectory = ResolveBuildOutputDirectory();
        var targetDirectory = Directory.CreateDirectory(Path.Combine(pluginsRoot, slug)).FullName;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(sourceFile, Path.Combine(targetDirectory, Path.GetFileName(sourceFile)), overwrite: true);
    }

    /// <summary>
    /// Mirrors <c>DepsJsonDependencyScan.ResolveDepsJsonPath</c>'s own approach (that class's own
    /// remarks): Configuration and TargetFramework are read off THIS TEST RUN's own output directory
    /// (<see cref="AppContext.BaseDirectory"/>) rather than hardcoded, since every project in the
    /// solution builds under the same configuration and (here) the same target framework. The repo
    /// root is found by walking up from there until <c>GenWave.sln</c> turns up — no environment
    /// variable or hardcoded absolute path needed, since this test project's own depth beneath the
    /// repo root is stable (every other repo-root-relative lookup in this suite already assumes it).
    /// </summary>
    static string ResolveBuildOutputDirectory()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

        var targetFrameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = targetFrameworkDirectory.Parent
            ?? throw new InvalidOperationException(
                $"\"{AppContext.BaseDirectory}\" has no Configuration directory above its TargetFramework one.");

        var buildOutputDirectory = Path.Combine(
            repoRoot, ExampleProjectRelativePath, "bin", configurationDirectory.Name, targetFrameworkDirectory.Name);

        if (!File.Exists(Path.Combine(buildOutputDirectory, ExampleAssemblyFileName)))
        {
            throw new DirectoryNotFoundException(
                $"No build output for the example plugin at \"{buildOutputDirectory}\" — expected " +
                $"\"{ExampleProjectRelativePath}\" to have built as part of this test run's own solution " +
                "build order (see GenWave.Plugins.Tests.csproj's own ProjectReference comment).");
        }

        return buildOutputDirectory;
    }

    static string FindRepoRoot(string startDirectory)
    {
        for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenWave.sln")))
                return current.FullName;
        }

        throw new InvalidOperationException($"Could not find GenWave.sln walking up from \"{startDirectory}\".");
    }
}
