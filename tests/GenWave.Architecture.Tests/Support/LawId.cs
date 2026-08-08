namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// Stable law identifiers (ARCHITECTURE.md "Architecture governance", F105.1's law table). Every
/// failure message and every <see cref="ArchitectureExemption"/> names one of these so a red test
/// and a baseline entry are unambiguously talking about the same law.
/// </summary>
internal static class LawId
{
    /// <summary>Inner projects (Core, Orchestration, Tts, Loudness) reference no ASP.NET, Npgsql,
    /// or Dapper assemblies.</summary>
    public const string L1 = "L1";

    /// <summary>Npgsql/Dapper types appear only in MediaLibrary's repository layer, with the
    /// composition root's <c>NpgsqlDataSource</c> construction as the named exemption.</summary>
    public const string L2 = "L2";

    /// <summary>The reference half of L4: <c>GenWave.Abstractions</c> references nothing beyond
    /// the BCL. (The immutability half is STORY-291's law, not this suite's.)</summary>
    public const string L4References = "L4-references";

    /// <summary>HttpClient confinement: <c>System.Net.Http.HttpClient</c> is constructed or asked
    /// for (typed-client injection, raw construction, <c>IHttpClientFactory.CreateClient</c>) only
    /// by the designated seam types (<see cref="HttpClientSeams"/>) — every outbound origin stays
    /// enumerable (SSRF surface control).</summary>
    public const string L3 = "L3";
}
