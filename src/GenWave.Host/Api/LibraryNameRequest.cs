namespace GenWave.Host.Api;

/// <summary>
/// Request body for POST /api/libraries (create) and PATCH /api/libraries/{id} (rename).
/// The <see cref="Name"/> field is nullable here so the controller can produce a typed
/// 400 instead of a 400 from the model binder when the field is missing or null.
/// Blank/whitespace validation is applied explicitly in the controller action.
/// </summary>
public sealed record LibraryNameRequest(string? Name);
