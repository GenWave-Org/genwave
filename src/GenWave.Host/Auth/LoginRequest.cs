using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Auth;

/// <summary>Request body for <c>POST /api/auth/login</c> — single config admin password.</summary>
public sealed record LoginRequest
{
    [Required] public string Password { get; init; } = "";
}
