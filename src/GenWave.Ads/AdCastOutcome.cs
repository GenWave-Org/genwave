namespace GenWave.Ads;

/// <summary>
/// What <see cref="AdCastPicker.Pick"/> actually did with the live <c>CastVoices</c> pool (SPEC
/// F167.2-F167.4; STORY-402; PLAN T415) — narrower than a full result type, the <see cref="AdRenderOutcome"/>
/// precedent one file over: exists only so <see cref="AdSpotWorker"/> knows whether to log the
/// degraded-pool cases, never to carry human-readable detail itself.
/// </summary>
internal enum AdCastOutcome
{
    /// <summary>Two distinct, non-announcer voices were available — the happy path, no log needed.</summary>
    Cast,

    /// <summary>Exactly one non-announcer candidate was available — VOICE1 and VOICE2 share that one
    /// voice (SPEC F167.2's own thin-pool degrade, PLAN T415 review R4).</summary>
    ThinPool,

    /// <summary>No non-announcer candidate was available at all — every tag, INCLUDING the announcer,
    /// falls back to the station's own voice (SPEC F167.4's literal wording, PLAN T415 review R4: "even
    /// if AnnouncerVoice is set").</summary>
    EmptyPool,
}
