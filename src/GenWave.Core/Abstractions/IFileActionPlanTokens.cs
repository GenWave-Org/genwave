using System.Diagnostics.CodeAnalysis;
using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Mints and reads the opaque plan token a dry-run response carries and a confirm request presents
/// back (SPEC F154.5; STORY-379; PLAN T379, gh-#529) — bound to the plan's
/// <c>(media id, xmin, from, to)</c> tuple and expiring 10 minutes after it was minted.
/// <c>MediaLibrary.Garden.FileActions.HmacFileActionPlanTokens</c> is this port's own T379
/// implementation (HMAC-SHA256, keyed per-process); T381 replaces the Host's registration with an
/// <c>IDataProtector</c>-backed one — callers depend on this interface, never the concrete type.
///
/// <para>
/// <b>One clock, supplied by the caller (T379 review N4):</b> both <see cref="Mint"/> and
/// <see cref="TryRead"/> take <c>now</c> as a parameter — an implementation never reads its own
/// clock, so mint-time and read-time can never silently observe two different notions of "now" (the
/// mint-side <see cref="TimeProvider"/> field the first cut of this codec carried was exactly that
/// hazard, even though every real caller happened to pass a consistent clock).
/// </para>
/// </summary>
public interface IFileActionPlanTokens
{
    /// <summary>Mints an opaque token binding <paramref name="plan"/>'s
    /// <c>(mediaId, xmin, verb, from, to, expiresAt)</c> — the dry-run response's <c>plan_token</c>.
    /// </summary>
    /// <param name="plan">The plan to bind. Must not already be expired as of <paramref name="now"/>.</param>
    /// <param name="now">The instant minting happens — never trusted from anywhere but the caller.</param>
    string Mint(FileActionPlan plan, DateTimeOffset now);

    /// <summary>
    /// Reads a token minted by <see cref="Mint"/>. Returns <see langword="true"/> with
    /// <paramref name="plan"/> populated only when the token's signature verifies, its payload
    /// parses, AND <paramref name="now"/> is strictly before the plan's <c>ExpiresAt</c> (at or after
    /// ⇒ <see cref="PlanTokenFailure.Expired"/>); otherwise returns <see langword="false"/> with
    /// <paramref name="failure"/> naming why (never echoing the token or any part of its payload).
    /// <paramref name="token"/> being <see langword="null"/> is itself
    /// <see cref="PlanTokenFailure.Invalid"/>, never a thrown exception (T379 review N3).
    /// </summary>
    bool TryRead(
        string token,
        DateTimeOffset now,
        [NotNullWhen(true)] out FileActionPlan? plan,
        out PlanTokenFailure failure);
}
