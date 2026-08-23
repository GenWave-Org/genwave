using System.ComponentModel.DataAnnotations;

namespace GenWave.Host.Options;

/// <summary>
/// The three F143.4 endpoint caps for <c>POST /api/announcements</c> (SPEC F143.4, STORY-357, PLAN
/// T339), shipped as settings-tunable defaults. Bound from the <c>Announcements</c> section — top
/// level, not nested under <c>Station</c>, matching <c>Announcements:TokenHash</c>'s own SPEC F145.3
/// key. Env/compose-only for now, the same <see cref="RequestsOptions"/> precedent: an
/// operator-tuned deployment knob, deliberately absent from
/// <see cref="GenWave.Host.Configuration.StationSettingsAllowlist"/> — a later task (e.g. PLAN T344's
/// admin page) may widen this into a live settings-API PUT the same way
/// <c>Station:Requests:*</c> widened three of <see cref="RequestsOptions"/>' own knobs, without
/// needing a shape change here. Top-level properties (unlike <c>StationOptions</c>' nested knobs), so
/// <c>.ValidateDataAnnotations()</c> genuinely enforces the <c>[Range]</c> attributes at boot — the
/// same "top-level binds, nested don't" boot-floor rule <c>RequestsOptions</c>' own remarks document.
/// </summary>
public sealed class AnnouncementsOptions
{
    public const string SectionName = "Announcements";

    /// <summary>Maximum accepted message length in characters (SPEC F143.4). Longer ⇒ 400, nothing
    /// written — mirrors <c>station.announcement</c>'s own 280 CHECK (db/40), which
    /// <see cref="GenWave.Core.Abstractions.IAnnouncementStore.InsertOrCollapseAsync"/>'s own remarks
    /// name as a defensive backstop for exactly the case this value is ever raised past 280.</summary>
    [Range(1, int.MaxValue)]
    public int MessageMaxChars { get; set; } = 280;

    /// <summary>Maximum ACCEPTED POSTs per rolling minute, STATION-WIDE — not per caller (SPEC F143.4:
    /// "limiter policy on this route only"). Enforced by <see cref="GenWave.Host.Api.AnnouncementAcceptedRateLimiter"/>
    /// — a singleton acquired IN-ACTION by <c>AnnouncementsController.Post</c>, after every refusal
    /// gate, never by a rate-limiter middleware policy (T339 review finding F1: a middleware policy
    /// runs before authentication, so it would count anonymous/refused requests against this same
    /// budget). Read ONCE at that singleton's construction (the <see cref="RequestsOptions"/>
    /// precedent: not live-monitored), since <see cref="System.Threading.RateLimiting.FixedWindowRateLimiter"/>
    /// has no live-reconfigure path once built.</summary>
    [Range(1, int.MaxValue)]
    public int AcceptedPerMinute { get; set; } = 6;

    /// <summary>Maximum simultaneously pending rows, station-wide (SPEC F143.4); at cap, a POST
    /// refuses 429 with nothing written — never an eviction (contrast <c>Requests:PendingCap</c>'s own
    /// evict-oldest shape for a best-effort listener wish: an announcement is a deliberate owner
    /// message, so the honest response is "wait", not "silently discard someone else's").</summary>
    [Range(1, int.MaxValue)]
    public int PendingDepthCap { get; set; } = 12;
}
