namespace GenWave.Host.Options;

/// <summary>
/// Configuration for the Admin subsystem. Bound from the <c>Admin</c> config section.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// How many entries the play history ring retains. Oldest entries are evicted
    /// once the ring is full. Default 50.
    /// </summary>
    public int PlayHistoryCapacity { get; init; } = 50;

    /// <summary>
    /// Single admin password for the Admin UI login (single-station deployment — no user table).
    /// When empty, the admin plane is fail-closed (SPEC F60.4/STORY-164): login always fails and no
    /// admin endpoint is reachable. Set via the <c>ADMIN_PASSWORD</c> environment variable.
    /// </summary>
    public string Password { get; init; } = "";

    /// <summary>Auth cookie lifetime in hours. Default 12.</summary>
    public int SessionLifetimeHours { get; init; } = 12;

    /// <summary>
    /// Auth cookie name. A plain name (no <c>__Host-</c> prefix) so the cookie also works over
    /// plain HTTP on localhost during development.
    /// </summary>
    public string CookieName { get; init; } = "genwave-auth";
}
