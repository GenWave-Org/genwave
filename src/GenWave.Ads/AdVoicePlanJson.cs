namespace GenWave.Ads;

using System.Text.Json;

/// <summary>
/// The ONE <see cref="JsonSerializerOptions"/> instance every reader and writer of the <c>voice_plan</c>
/// jsonb column shares (SPEC F167; STORY-402; PLAN T415) — <c>camelCase</c>
/// (<see cref="JsonSerializerDefaults.Web"/>) is load-bearing, not a style choice:
/// <c>VoicePackRepository</c>'s uninstall-guard SQL joins on the literal key
/// <c>e-&gt;&gt;'voiceId'</c> (<see cref="AdVoicePlanEntry.VoiceId"/>), so anything that serializes a
/// plan with different casing silently breaks that guard. <see cref="AdSpotWorker"/> (stamping a freshly
/// cast plan), <see cref="AdRenderService"/> (parsing one back off a claimed row), and
/// <c>GenWave.Host.Api.AdsController</c> (the owner-draft read/write path) all go through this one type
/// so the casing can never drift between them again.
/// </summary>
public static class AdVoicePlanJson
{
    public static readonly JsonSerializerOptions Options = BuildOptions();

    public static string Serialize(IReadOnlyList<AdVoicePlanEntry> plan) =>
        JsonSerializer.Serialize(plan, Options);

    static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // populateMissingResolver: true — this options instance uses reflection-based (de)serialization,
        // not a source-generated JsonSerializerContext, so it has no TypeInfoResolver of its own yet;
        // MakeReadOnly() refuses to lock the instance without one. This fills in the default reflection
        // resolver, then locks it, the same way ASP.NET Core's own default JsonOptions ends up read-only.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
