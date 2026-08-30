using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Api;

/// <summary>
/// The Host's own <see cref="IFileActionPlanTokens"/> (SPEC F154.5; STORY-379; PLAN T381, gh-#529)
/// — REPLACES <c>GenWave.MediaLibrary</c>'s <c>HmacFileActionPlanTokens</c> registration (a
/// per-process random key, T379's own placeholder — see that class's own remarks) with the SAME
/// <see cref="IDataProtectionProvider"/> the admin cookie is already keyed with
/// (<c>AdminApiServiceCollectionExtensions</c>'s own <c>AddDataProtection().PersistKeysToFileSystem(...)</c>)
/// — a plan token now survives an api container recreate, and a dry-run/confirm pair straddling a
/// rolling deploy never breaks.
///
/// <para>
/// <b>Payload:</b> the <see cref="FileActionPlan"/> record, serialised via
/// <see cref="JsonSerializer"/> — its own DECLARED property order (MediaId, Xmin, Verb, From, To,
/// TagDiff, ExpiresAt) is fixed by the record's own definition, never reordered by anything in this
/// file. <see cref="IDataProtector.Protect"/> both encrypts AND authenticates the payload (AES +
/// HMAC under the hood) — unlike <c>HmacFileActionPlanTokens</c>'s own signed-but-plaintext base64url
/// payload, the paths inside travel opaque to anyone without this process's own key ring.
/// </para>
///
/// <para>
/// <see cref="TryRead"/> maps ANY <see cref="CryptographicException"/> from
/// <see cref="IDataProtector.Unprotect"/> — a tampered token, one minted under a rotated-away key,
/// or simple garbage — onto <see cref="PlanTokenFailure.Invalid"/>, then applies the SAME
/// <c>now &gt;= plan.ExpiresAt</c> expiry rule <c>HmacFileActionPlanTokens</c> uses (F154.5's
/// 10-minute window closes AT its own edge, never one instant past it).
/// </para>
/// </summary>
sealed class DataProtectionFileActionPlanTokens : IFileActionPlanTokens
{
    /// <summary>The <see cref="IDataProtectionProvider.CreateProtector(string)"/> purpose string —
    /// distinct from every other purpose this process's key ring already serves (the admin cookie),
    /// so key derivation can never collide across the two uses.</summary>
    const string Purpose = "GenWave.Gardener.FileActionPlan";

    readonly IDataProtector protector;

    public DataProtectionFileActionPlanTokens(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        protector = provider.CreateProtector(Purpose);
    }

    public string Mint(FileActionPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (now >= plan.ExpiresAt)
            throw new ArgumentException("Cannot mint a token for a plan that has already expired.", nameof(plan));

        var payload = JsonSerializer.SerializeToUtf8Bytes(plan);
        var protectedBytes = protector.Protect(payload);
        return WebEncoders.Base64UrlEncode(protectedBytes);
    }

    public bool TryRead(
        string token, DateTimeOffset now, [NotNullWhen(true)] out FileActionPlan? plan, out PlanTokenFailure failure)
    {
        plan = null;
        failure = PlanTokenFailure.Invalid;

        // A null token is a caller bug, not a crash (mirrors HmacFileActionPlanTokens' own T379
        // review N3 posture) — this port's contract is a plain, non-nullable string, but a less
        // careful caller (JSON model binding, reflection) can still hand one in.
        if (token is null) return false;

        byte[] payload;
        try
        {
            var protectedBytes = WebEncoders.Base64UrlDecode(token);
            payload = protector.Unprotect(protectedBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }

        FileActionPlan? parsedPlan;
        try
        {
            parsedPlan = JsonSerializer.Deserialize<FileActionPlan>(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsedPlan is null) return false;

        if (now >= parsedPlan.ExpiresAt)
        {
            failure = PlanTokenFailure.Expired;
            return false;
        }

        plan = parsedPlan;
        failure = PlanTokenFailure.None;
        return true;
    }
}
