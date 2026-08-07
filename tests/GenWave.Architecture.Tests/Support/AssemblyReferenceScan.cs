using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L1 and L4-references are "never/only ever reference assembly family X" checks over an
/// open-ended family — the whole ASP.NET Core shared framework for L1 (dozens of
/// <c>Microsoft.AspNetCore.*</c> assemblies), "anything that isn't the BCL" for L4. Both were
/// evaluated against ArchUnitNET's dependency selectors first: <c>NotDependOnAny</c> only knows
/// about assemblies explicitly passed to <c>ArchLoader.LoadAssemblies</c> — verified experimentally
/// at T211 (a fixture type calling into an unloaded assembly's member evaluated as a false-pass
/// until that assembly was loaded). Sound for L2 below, where the forbidden set is exactly two
/// well-known assemblies (Npgsql, Dapper) that can simply always be loaded; unsound here, where the
/// forbidden/allowed set can't be exhaustively enumerated up front without silently under-covering
/// a family this suite doesn't yet know about.
///
/// Reading the compiled assembly's own AssemblyRef metadata table directly is exhaustive over
/// DIRECT references by construction instead: every assembly the compiler baked into the reference
/// is listed, nothing to enumerate or keep in sync as the shared framework grows. (It stops at
/// direct references — a transitive dependency pulled in only through a package's own dependency
/// graph, never itself named in an AssemblyRef here, would need the deps.json-based technique
/// <see cref="DepsJsonDependencyScan"/> uses for L4-references instead.)
/// </summary>
internal static class AssemblyReferenceScan
{
    /// <summary>Every assembly referenced by the assembly at <paramref name="assemblyPath"/> whose
    /// simple name matches <paramref name="isForbidden"/>.</summary>
    public static IReadOnlyList<string> ForbiddenReferences(string assemblyPath, Func<string, bool> isForbidden)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();

        var found = new List<string>();
        foreach (var handle in metadataReader.AssemblyReferences)
        {
            var name = metadataReader.GetString(metadataReader.GetAssemblyReference(handle).Name);
            if (isForbidden(name))
                found.Add(name);
        }

        return found;
    }
}
