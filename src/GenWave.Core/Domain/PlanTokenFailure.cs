namespace GenWave.Core.Domain;

/// <summary>
/// Why <see cref="Abstractions.IFileActionPlanTokens.TryRead"/> could not return a plan (SPEC F154.5;
/// STORY-379; PLAN T379, gh-#529). The confirm endpoint (T381) maps <see cref="Expired"/> and
/// <see cref="Invalid"/> onto the same 409 shape STORY-379 AC7/AC14 both expect — neither ever
/// echoes the token or any part of its payload.
/// </summary>
public enum PlanTokenFailure
{
    /// <summary>The token was read successfully — paired with <c>TryRead</c> returning
    /// <see langword="true"/>, never surfaced as a failure itself.</summary>
    None,

    /// <summary>The token is malformed, its signature does not verify (tampered, or signed with a
    /// different key), or its payload does not parse into a well-formed plan.</summary>
    Invalid,

    /// <summary>The token verified and parsed, but the caller's <c>now</c> is AT OR AFTER its
    /// <c>ExpiresAt</c> (T379 review N4: <c>now &gt;= ExpiresAt</c>, never the strict
    /// <c>now &gt; ExpiresAt</c> the field's own name would otherwise invite — the plan token's
    /// horizon is a closed-at-the-end window) — the 10-minute window (F154.5) has closed.</summary>
    Expired,
}
