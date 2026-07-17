namespace GenWave.Host.Auth;

/// <summary>
/// Response item for <c>GET /api/libraries</c> and the POST 201 body.
/// <see cref="MediaCount"/> is 0 on a freshly-created library.
/// </summary>
public sealed record LibraryDto(long Id, string Name, int MediaCount);
