namespace GenWave.Host.Tests.Support;

/// <summary>
/// Locates <c>examples/genwave-plugin-example</c>'s own <c>dotnet build</c> output and copies it into
/// a fresh directory shaped exactly like <c>{Plugins:Root}/&lt;slug&gt;/</c> — the same shape a real
/// operator's <c>compose.plugins.yaml</c> mount produces (PLAN T394, STORY-386). Mirrors
/// <c>GenWave.Plugins.Tests.Support.ExamplePluginPayload</c>'s own mechanism and its own remarks on
/// why the duplication (rather than a shared helper) is deliberate: two SEPARATE test assemblies each
/// need their own hermetic, un-referenced copy of the example's build output, and pulling either one
/// into the other's dependency graph just to save a few lines is worse coupling than the duplication
/// costs (that class's own remarks on the identical trade-off one project over).
///
/// <c>GenWave.Host.Tests.csproj</c>'s own <c>ReferenceOutputAssembly="false" Private="false"</c>
/// project reference is the OTHER half of this: it forces the example project to
/// build, fresh, as part of this project's own build order, without ever adding its assembly to this
/// project's own compile references or copied output — the example's own assembly loads for real
/// exactly once, by <c>GenWave.Plugins.PluginLoader</c>, inside its own dedicated
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> (SPEC F156.3), never into this test
/// process's own <c>AssemblyLoadContext.Default</c>.
/// </summary>
internal static class ExamplePluginBuildOutput
{
    const string ExampleProjectRelativePath = "examples/genwave-plugin-example";
    const string ExampleAssemblyFileName = "ExamplePlugin.dll";

    /// <summary>
    /// Copies every file from the example project's own build output — its assembly, symbols, and the
    /// <c>plugin.json</c> its own <c>.csproj</c> copies alongside them — into
    /// <c>"{pluginsRoot}/{slug}/"</c>, creating both directories as needed.
    /// </summary>
    public static void CopyInto(string pluginsRoot, string slug)
    {
        var sourceDirectory = ResolveBuildOutputDirectory();
        var targetDirectory = Directory.CreateDirectory(Path.Combine(pluginsRoot, slug)).FullName;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(sourceFile, Path.Combine(targetDirectory, Path.GetFileName(sourceFile)), overwrite: true);
    }

    /// <summary>
    /// Configuration and TargetFramework are read off THIS TEST RUN's own output directory
    /// (<see cref="AppContext.BaseDirectory"/>) rather than hardcoded, since every project in the
    /// solution builds under the same configuration and (here) the same target framework — mirrors
    /// <c>GenWave.Plugins.Tests.Support.ExamplePluginPayload.ResolveBuildOutputDirectory</c>'s own
    /// approach exactly.
    /// </summary>
    static string ResolveBuildOutputDirectory()
    {
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);

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
                "build order (see GenWave.Host.Tests.csproj's own ProjectReference comment).");
        }

        return buildOutputDirectory;
    }
}
