namespace GenWave.Tts;

/// <summary>Seeds <c>kokoro</c>/<c>piper</c> from the legacy Tts keys when <c>Tts:Engines</c> has no entry of that name (SPEC F219.1).</summary>
static class VoiceEngineSeeding
{
    public static void Seed(VoiceEnginesOptions options, TtsOptions tts)
    {
        if (!options.Engines.ContainsKey(VoiceEngineProfiles.KokoroName))
        {
            options.Engines[VoiceEngineProfiles.KokoroName] = new VoiceEngineEntry
            {
                Endpoint = tts.Endpoint,
                Profile = VoiceEngineProfiles.KokoroName,
                RenderBudgetSeconds = tts.RenderBudgetSeconds,
            };
        }

        if (!string.IsNullOrEmpty(tts.PiperPrimaryEndpoint)
            && !options.Engines.ContainsKey(VoiceEngineProfiles.PiperName))
        {
            options.Engines[VoiceEngineProfiles.PiperName] = new VoiceEngineEntry
            {
                Endpoint = tts.PiperPrimaryEndpoint,
                Profile = VoiceEngineProfiles.PiperName,
                RenderBudgetSeconds = tts.RenderBudgetSeconds,
            };
        }
    }
}
