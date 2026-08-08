using System.Globalization;
using System.Text;
using GenWave.Host.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace GenWave.SeamIndexGenerator;

/// <summary>
/// T216 (SEAMS.md generator, STORY-294, SPEC F105.6): the ONE piece of code that turns the
/// composition root's real <see cref="IServiceCollection"/> into the committed root <c>SEAMS.md</c>
/// text — called identically by this tool's <c>Program.cs</c> (writes the file, T216/T217's CI
/// regeneration) and by <c>GenWave.Architecture.Tests</c> (byte-compares against the committed file,
/// T216's AC1/AC2 facts). Two runs produce byte-identical output: no timestamps, no machine paths, no
/// <see cref="Type.AssemblyQualifiedName"/> (see <see cref="FriendlyTypeName"/>), LF line endings,
/// ordinal (never culture-sensitive) sorting AND formatting throughout — every interpolated number
/// is explicit <see cref="CultureInfo.InvariantCulture"/>, not the ambient current culture.
/// </summary>
public static class SeamIndexDocument
{
    public static string Generate()
    {
        var ports = SeamCompositionSnapshot.Capture(IsGenWavePort);

        var byBindingSite = ports
            .GroupBy(p => EffectiveAdapter(p).AdapterType.Assembly.GetName().Name ?? "(unknown assembly)")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var body = new StringBuilder();
        foreach (var group in byBindingSite)
            AppendSection(body, group.Key, group.OrderBy(p => FriendlyTypeName.Of(p.PortType), StringComparer.Ordinal).ToList());

        var document = new StringBuilder();
        document.Append(Header(ports.Count, byBindingSite.Count));
        document.Append(body);

        // Deterministic regardless of the OS the generator runs on; normalize any stray CRLF a
        // future edit might introduce, then guarantee exactly one trailing newline.
        return document.ToString().Replace("\r\n", "\n").TrimEnd('\n') + "\n";
    }

    static bool IsGenWavePort(Type type) =>
        type.IsInterface
        && type.Namespace is { } ns
        && (ns == "GenWave" || ns.StartsWith("GenWave.", StringComparison.Ordinal));

    static SeamAdapterEntry EffectiveAdapter(SeamPort port) => port.Adapters.Single(a => a.IsEffective);

    static string Header(int seamCount, int projectCount)
    {
        var seams = seamCount.ToString(CultureInfo.InvariantCulture);
        var projects = projectCount.ToString(CultureInfo.InvariantCulture);
        var projectWord = projectCount == 1 ? "project" : "projects";

        return $"""
            # SEAMS.md

            > **Generated. Never hand-edit.** Produced by `tools/SeamIndexGenerator` from the ACTUAL
            > DI registrations GenWave.Host's composition root (`Program.cs`) builds — every seam below
            > was resolved from a live `IServiceCollection`/`IServiceProvider`
            > (`WebApplicationFactory<Program>`, no Kestrel, no Postgres/Liquidsoap/Kokoro/Ollama/Icecast
            > reached), never re-typed by hand. Regenerate: `dotnet run --project tools/SeamIndexGenerator`.
            > Regenerated and byte-diffed by CI (SPEC F105.6, PLAN T217 — wired in that task, this PR's
            > second half) — a new or changed seam shipped without a regenerated index will be a red check.
            >
            > **Check this file before adding a seam** — extend or decorate an existing port before
            > minting a near-duplicate.
            >
            > **Scope & method.** One row per GenWave.* interface port the composition root registers —
            > port → default adapter → binding site, grouped by section below. "Binding site" is the
            > PROJECT that owns the effective (last-registered) adapter, not the specific `Add*` call:
            > nothing on a `ServiceDescriptor` records which extension method added it, so per-registration
            > attribution is impractical — this generator attributes honestly at project granularity
            > instead of guessing. The Adapter/Lifetime columns are always the port's LAST registration —
            > what a plain `IServiceProvider.GetService<T>()` call actually returns. A port registered
            > more than once also lists every earlier registration in its Notes column, honestly labeled
            > "also registered" rather than "overridden": nothing on a `ServiceDescriptor` records whether
            > a later registration is a `TryAdd`-default override (single-resolve wins, e.g.
            > `IPersonaPickProvider`) or one leg of a fan-out consumed via `IEnumerable<T>`/`GetServices<T>()`
            > where every registration stays active (e.g. `IDependencyProbe`'s three health probes) — read
            > the call site to tell which.
            >
            > **Decorators.** Notes also lists "wraps: ..." where a decorator chain is mechanically
            > derivable — a constructor parameter typed as a CONCRETE class implementing the same port
            > (e.g. `DegradationGatedCopyWriter`'s `ISegmentCopyWriter` wraps both `LlmCopyWriter` and
            > `TemplateCopyWriter` directly). It is NOT always derivable: `ITtsSynthesizer` is a real,
            > three-deep chain (`NormalizingTtsSynthesizer` wraps `FallbackTtsSynthesizer` wraps
            > Kokoro/Piper) that this generator cannot see past — every hop there is an INTERFACE-typed
            > constructor parameter (`ITtsSynthesizer inner`/`primary`) whose actual concrete argument is
            > chosen inside a hand-written factory closure, not reflectable metadata. `ITtsVoiceLister`
            > (`CachedVoiceLister` wrapping Kokoro) has the identical shape. A row with no "wraps:" note
            > may still be layered — read `TtsServiceCollectionExtensions.cs`'s own registration comments
            > (and its siblings) for the ground truth a generator this size cannot fully mechanize.
            >
            > Enumerated under this repo's `Development` environment defaults
            > (`appsettings.Development.json`) plus a placeholder `ConnectionStrings:Library` — the same
            > minimal, DB-free config `GenWave.Host.Tests` already proves is enough for the composition
            > root to build cleanly. Program.cs registers its whole graph unconditionally (no
            > environment- or flag-gated `Add*` branch exists today), so nothing is known to be missing
            > from this map for that reason.
            >
            > **{seams} seams across {projects} {projectWord}.**

            """;
    }

    static void AppendSection(StringBuilder sb, string projectName, IReadOnlyList<SeamPort> ports)
    {
        sb.Append("\n## ").Append(projectName).Append(" (").Append(ports.Count)
            .Append(ports.Count == 1 ? " seam)\n\n" : " seams)\n\n");
        sb.Append("| Port | Adapter | Lifetime | Notes |\n");
        sb.Append("|---|---|---|---|\n");

        foreach (var port in ports)
            AppendRow(sb, port);
    }

    static void AppendRow(StringBuilder sb, SeamPort port)
    {
        var effective = EffectiveAdapter(port);
        var others = port.Adapters.Where(a => !a.IsEffective).ToList();
        var chain = DecoratorChain.Derive(effective.AdapterType, port.PortType);

        var noteParts = new List<string>();
        if (chain.Count > 0)
        {
            noteParts.Add("wraps: " + string.Join(", ", chain.Select(t =>
                $"`{FriendlyTypeName.Of(t)}` ({t.Assembly.GetName().Name})")));
        }
        if (others.Count > 0)
        {
            noteParts.Add("also registered: " + string.Join(", ", others.Select(a =>
                $"`{FriendlyTypeName.Of(a.AdapterType)}` ({a.AdapterType.Assembly.GetName().Name})")));
        }

        var notes = noteParts.Count == 0 ? "—" : string.Join("; ", noteParts);

        sb.Append("| `").Append(FriendlyTypeName.Of(port.PortType)).Append("` ");
        sb.Append("| `").Append(FriendlyTypeName.Of(effective.AdapterType)).Append("` ");
        sb.Append("| ").Append(effective.Lifetime).Append(' ');
        sb.Append("| ").Append(notes).Append(" |\n");
    }
}
