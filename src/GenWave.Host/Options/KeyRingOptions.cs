namespace GenWave.Host.Options;

/// <summary>
/// Configuration for ASP.NET Core Data Protection key-ring persistence (F14.3, F11.11).
/// The key ring is written to a mounted Docker volume (<c>dp_keys</c>) so session cookies
/// survive <c>api</c> container recreation.
///
/// Named <c>KeyRingOptions</c> rather than <c>DataProtectionOptions</c> to avoid colliding with
/// <c>Microsoft.AspNetCore.DataProtection.DataProtectionOptions</c> when both are in scope.
/// </summary>
public sealed class KeyRingOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Filesystem path for the Data Protection key ring. Must be on a persistent volume in
    /// production; defaults to the well-known container mount path.
    /// </summary>
    public string KeyRingPath { get; init; } = "/var/lib/genwave/dp-keys";
}
