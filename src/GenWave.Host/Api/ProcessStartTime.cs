namespace GenWave.Host.Api;

/// <summary>
/// The process's boot instant, captured exactly once. Program.cs constructs the single instance
/// immediately after <c>WebApplication.CreateBuilder</c> and registers it as a singleton, so
/// <see cref="Value"/> reflects true process start — not the instant of the first request that
/// happens to resolve it (a lazily-constructed DI singleton would otherwise drift). Consumed by
/// <see cref="StatusController"/> for the <c>startedAt</c> field of <c>GET /api/status</c>
/// (SPEC F28.6).
/// </summary>
public sealed record ProcessStartTime(DateTimeOffset Value);
