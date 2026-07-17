namespace GenWave.Tts.Tests;

/// <summary>One request as seen by <see cref="MockCompletionsServer"/> — raw JSON body plus the
/// Authorization header (empty string when absent), for prompt-contract and bearer-header specs.</summary>
sealed record CapturedCompletionRequest(string Body, string AuthorizationHeader);
