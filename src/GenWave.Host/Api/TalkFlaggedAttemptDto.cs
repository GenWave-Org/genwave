namespace GenWave.Host.Api;

/// <summary>One flagged attempt: when (UTC), the DJ (or sponsor), the kind slug, the words flagged and the outcome token (<c>re-asked</c>, <c>accepted</c>, ...).</summary>
public sealed record TalkFlaggedAttemptDto(long CheckId, DateTimeOffset When, string? Dj, string Kind, string Words, string Outcome);
