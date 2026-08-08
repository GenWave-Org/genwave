using System.Reflection;
using System.Text.RegularExpressions;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// Stable law identifiers (ARCHITECTURE.md "Architecture governance", F105.1's law table). Every
/// failure message and every <see cref="ArchitectureExemption"/> names one of these so a red test
/// and a baseline entry are unambiguously talking about the same law.
/// </summary>
internal static class LawId
{
    /// <summary>The shape every law id above takes — <c>L</c>, one or more digits, optionally a
    /// lowercase <c>-word</c> suffix (<c>L1</c>...<c>L6</c>, <c>L4-references</c>,
    /// <c>L4-immutability</c>) — owned here, once, so <see cref="All"/>'s own const-filter (below)
    /// and <see cref="ContributingLawTable"/>'s doc-parser both consume the SAME shape definition
    /// instead of each hand-rolling their own copy that could quietly drift apart (STORY-293
    /// review). <c>L\d+</c>, not <c>L\d</c>: a tenth law (<c>L10</c>) must still match.</summary>
    public const string IdPattern = @"^L\d+(-[a-z]+)?$";

    /// <summary>Inner projects (Core, Orchestration, Tts, Loudness) reference no ASP.NET, Npgsql,
    /// or Dapper assemblies.</summary>
    public const string L1 = "L1";

    /// <summary>Npgsql/Dapper types appear only in MediaLibrary's repository layer, with the
    /// composition root's <c>NpgsqlDataSource</c> construction as the named exemption.</summary>
    public const string L2 = "L2";

    /// <summary>HttpClient confinement: <c>System.Net.Http.HttpClient</c> is constructed or asked
    /// for (typed-client injection, raw construction, <c>IHttpClientFactory.CreateClient</c>) only
    /// by the designated seam types (<see cref="HttpClientSeams"/>) — every outbound origin stays
    /// enumerable (SSRF surface control).</summary>
    public const string L3 = "L3";

    /// <summary>The reference half of L4: <c>GenWave.Abstractions</c> references nothing beyond
    /// the BCL. (The immutability half is <see cref="L4Immutability"/>.)</summary>
    public const string L4References = "L4-references";

    /// <summary>The immutability half of L4: every public type in <c>GenWave.Abstractions</c>
    /// carries no publicly settable state — no non-init property setter, no mutable public field.
    /// (The reference half is <see cref="L4References"/>.)</summary>
    public const string L4Immutability = "L4-immutability";

    /// <summary>Host namespace discipline (gh-#399): <c>GenWave.Host</c> contains no type whose
    /// namespace is, or is nested under, an entry in <see cref="HostReservedNamespaces"/> — the
    /// graduated/reserved subsystem tripwire the Host graduation rule (SPEC F105.4) depends on.</summary>
    public const string L5 = "L5";

    /// <summary>Seam-placement mechanics: <c>GenWave.Abstractions</c> references no
    /// <c>GenWave.Core</c> type — the encodable half of the gh-#400 seam-placement criterion.</summary>
    public const string L6 = "L6";

    /// <summary>Every law id above, discovered by reflection over this type's own <c>public const
    /// string</c> fields rather than hand-listed a second time anywhere. STORY-293's carry-forward
    /// (PLAN T215): this is now the SINGLE source both Story290_DependencyLaws.cs's exemption-id
    /// whitelist and the suite↔doc parity test (<see cref="LawParity"/>) derive from — adding an
    /// eighth law const above is enough, on its own, to keep both in sync; neither keeps its own copy
    /// of the id list to forget to update.
    ///
    /// The filter is two-layered so it stays honest even as this class grows: <see cref="IdPattern"/>
    /// itself is excluded by name (it is a <c>public const string</c> too, but it names a SHAPE, not a
    /// law), and every surviving candidate is additionally required to actually MATCH
    /// <see cref="IdPattern"/> — so a future, unrelated <c>public const string</c> added to this class
    /// for some other reason can never silently masquerade as an eighth law id.</summary>
    public static IReadOnlyList<string> All { get; } = typeof(LawId)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name != nameof(IdPattern))
        .Select(field => field.GetRawConstantValue())
        .OfType<string>()
        .Where(value => Regex.IsMatch(value, IdPattern))
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToList();
}
