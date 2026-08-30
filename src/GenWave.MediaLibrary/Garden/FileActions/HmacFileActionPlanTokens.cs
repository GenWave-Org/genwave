using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace GenWave.MediaLibrary.Garden.FileActions;

/// <summary>
/// The T379 <see cref="IFileActionPlanTokens"/> implementation (SPEC F154.5; STORY-379; PLAN T379,
/// gh-#529) — HMAC-SHA256 over a canonical UTF-8 payload
/// <c>mediaId|xmin|verb|from|to|expiresAt</c> (<see cref="BuildPayload"/>), constant-time verified
/// (<see cref="CryptographicOperations.FixedTimeEquals"/>). The token is
/// <c>base64url(payload) + "." + base64url(signature)</c> — the payload travels WITH the token
/// (never a bare signature over caller-supplied fields) so <see cref="TryRead"/> can reconstruct the
/// whole plan from the token alone. <see cref="TagChange"/>s are NOT part of the payload — only the
/// F154.5 binding tuple <c>(mediaId, xmin, from, to)</c> plus <c>verb</c>/<c>expiresAt</c> travel — so
/// a plan read back always carries an empty <see cref="FileActionPlan.TagDiff"/>; a confirm step
/// re-derives the diff from the (freshly re-checked) catalog rather than trusting a value that could
/// be up to 10 minutes stale.
///
/// <para>
/// <b>Every field is base64url-encoded BEFORE being joined with <c>|</c></b> (T379 review N1) — the
/// base64url alphabet contains neither <c>|</c> nor <c>.</c>, so a path (or an <c>xmin</c>, in
/// principle) that itself contains either byte can never desynchronise the split on either side:
/// the outer <c>payload.signature</c> split and the inner per-field <c>|</c> split are both immune,
/// not merely "unlikely to collide" the way an un-encoded join would have been.
/// </para>
///
/// <para>
/// <b>Parsing still fails closed</b>: a payload that does not split into exactly 6 fields, or any
/// field that fails to base64url-decode, is <see cref="PlanTokenFailure.Invalid"/> outright.
/// </para>
///
/// <para>
/// <b>One clock (T379 review N4):</b> <see cref="Mint"/>/<see cref="TryRead"/> both take
/// <c>now</c> as a parameter — this class holds no <see cref="TimeProvider"/> of its own. Expiry is
/// <c>now &gt;= plan.ExpiresAt</c> (at-or-after, never the strict <c>&gt;</c> a first cut of this
/// class used) — the 10-minute window closes AT its own edge, not one instant past it.
/// </para>
///
/// <para>
/// T381 replaces this class's registration in <c>GenWave.Host</c> with an
/// <c>IDataProtector</c>-backed <see cref="IFileActionPlanTokens"/> — this implementation exists so
/// the binding-and-expiry SHAPE is pinned now, at T379, ahead of that swap.
/// </para>
/// </summary>
sealed class HmacFileActionPlanTokens : IFileActionPlanTokens
{
    /// <summary>The shortest HMAC-SHA256 key this class accepts (T379 review N5) — SHA-256's own
    /// block/output size; a shorter key would be pointlessly weaker than the algorithm it is used
    /// with.</summary>
    internal const int MinKeyBytes = 32;

    const char FieldSeparator = '|';
    const char SegmentSeparator = '.';
    const int ExpectedFieldCount = 6;

    readonly byte[] key;

    public HmacFileActionPlanTokens(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < MinKeyBytes)
            throw new ArgumentException($"The HMAC key must be at least {MinKeyBytes} bytes.", nameof(key));

        // A defensive copy (T379 review N5) — the caller's own array must never be able to mutate
        // this instance's signing key after construction.
        this.key = [.. key];
    }

    public string Mint(FileActionPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (now >= plan.ExpiresAt)
            throw new ArgumentException("Cannot mint a token for a plan that has already expired.", nameof(plan));

        var payloadBytes = Encoding.UTF8.GetBytes(BuildPayload(plan));
        var signature = ComputeHmac(payloadBytes);

        return string.Concat(
            WebEncoders.Base64UrlEncode(payloadBytes),
            SegmentSeparator,
            WebEncoders.Base64UrlEncode(signature));
    }

    public bool TryRead(
        string token, DateTimeOffset now, [NotNullWhen(true)] out FileActionPlan? plan, out PlanTokenFailure failure)
    {
        plan = null;
        failure = PlanTokenFailure.Invalid;

        // A null token is a caller bug, not a crash (T379 review N3) — this port's contract is a
        // plain, non-nullable string, but a less careful caller (JSON model binding, reflection)
        // can still hand one in.
        if (token is null) return false;

        var segments = token.Split(SegmentSeparator);
        if (segments.Length != 2) return false;

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = WebEncoders.Base64UrlDecode(segments[0]);
            signature = WebEncoders.Base64UrlDecode(segments[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(signature, ComputeHmac(payloadBytes)))
            return false;

        if (!TryParsePlan(Encoding.UTF8.GetString(payloadBytes), out var parsedPlan))
            return false;

        if (now >= parsedPlan.ExpiresAt)
        {
            failure = PlanTokenFailure.Expired;
            return false;
        }

        plan = parsedPlan;
        failure = PlanTokenFailure.None;
        return true;
    }

    byte[] ComputeHmac(byte[] payload)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(payload);
    }

    static string BuildPayload(FileActionPlan plan) => string.Join(
        FieldSeparator,
        EncodeField(plan.MediaId.ToString(CultureInfo.InvariantCulture)),
        EncodeField(plan.Xmin),
        EncodeField(plan.Verb.ToString()),
        EncodeField(plan.From),
        EncodeField(plan.To),
        EncodeField(plan.ExpiresAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));

    static bool TryParsePlan(string payload, [NotNullWhen(true)] out FileActionPlan? plan)
    {
        plan = null;
        var fields = payload.Split(FieldSeparator);
        if (fields.Length != ExpectedFieldCount) return false;

        string[] decoded;
        try
        {
            decoded = fields.Select(DecodeField).ToArray();
        }
        catch (FormatException)
        {
            return false;
        }

        if (!long.TryParse(decoded[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mediaId))
            return false;
        if (!Enum.TryParse<FileActionVerb>(decoded[2], out var verb) || !Enum.IsDefined(verb))
            return false;
        if (!long.TryParse(decoded[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAtMs))
            return false;

        plan = new FileActionPlan(
            mediaId,
            decoded[1],
            verb,
            decoded[3],
            decoded[4],
            [],
            DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs));
        return true;
    }

    static string EncodeField(string value) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));

    static string DecodeField(string value) => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value));
}
