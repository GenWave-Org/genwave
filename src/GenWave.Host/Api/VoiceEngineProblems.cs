using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>The one 400 for an engine name the voice-engine registry does not hold (SPEC F219.10), shared by every route that names one.</summary>
internal static class VoiceEngineProblems
{
    internal static ProblemDetails Unknown() => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title  = "Unknown voice engine.",
        Detail = "engine must name a configured voice engine.",
    };
}
