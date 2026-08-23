using System.Threading.Channels;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.Host.Options;

namespace GenWave.Host.Announcements;

/// <summary>
/// The host's <see cref="IStationEventSink"/> binding for the private→public flip's decline sweep
/// (SPEC F145.2, STORY-359, PLAN T343). Reacts to <see cref="SettingChanged"/> — the SAME event
/// <c>GenWave.Host.Configuration.StationSettingsStore.WriteAsync</c> already publishes for every
/// allowlisted write (gitea-#246, key only, never the value) — rather than an
/// <see cref="IOptionsMonitor{TOptions}.OnChange"/> subscription: this station's PUT
/// <c>/api/settings</c> write path already reaches every <see cref="IStationEventSink"/> consumer
/// through this exact fan-out, so reusing it needs no second subscription mechanism and no async-void
/// callback (an <c>OnChange</c> handler wanting to await real I/O would need one).
///
/// <para>
/// <b>No previous-value tracking needed.</b> <see cref="SettingChanged"/> fires only from a genuine
/// live WRITE to <c>Station:SpectatorMode</c> — never at boot (nothing calls
/// <c>StationSettingsStore.WriteAsync</c> merely to read a config default) — so every time this key's
/// <see cref="SettingChanged"/> arrives with the LIVE value read back as <see langword="true"/>, that
/// write just turned the station public. Because <see cref="Publish"/> only enqueues on that
/// direction (never on a write that lands <see langword="false"/>), and
/// <see cref="AnnouncementPrivacyFlipDrainService"/>'s own decline sweep is idempotent (declining an
/// already-empty live set is a harmless no-op), a redundant re-write of <see langword="true"/> while
/// already public costs nothing — there is no window where mis-firing on a repeat write could decline
/// something it shouldn't.
/// </para>
///
/// <para>
/// <b>The boot case (T343 review, ruled acceptable — no fix landed here).</b> A station that boots
/// DIRECTLY into <c>Station:SpectatorMode = true</c> with <c>pending</c>/<c>claimed</c> rows left over
/// from a previous private session never fires <see cref="SettingChanged"/> for this key at all (no
/// live write happens — the value simply loads as part of configuration binding), so this sink never
/// declines those rows on boot; they resolve later and VISIBLY, never silently — either
/// <c>AnnouncementLifecycleGuardianService</c>'s own TTL sweep expires them, or, defense-in-depth,
/// <c>SpectatorModeAnnouncementVendGuard</c> refuses to vend a private-past row while public regardless
/// of state, so nothing from before the boot can ever reach air.
/// </para>
///
/// <para>
/// The actual decline (<see cref="IAnnouncementLifecycle.DeclineAllLiveAsync"/>) is genuine async
/// Postgres I/O — <see cref="IStationEventSink"/>'s own contract ("MUST NOT throw and MUST return
/// promptly") means <see cref="Publish"/> only ever does the cheap, synchronous part (the key check
/// and the live options read) and hands off to <see cref="AnnouncementPrivacyFlipDrainService"/> via
/// a bounded queue — the SAME split <see cref="AnnouncementAiredEventSink"/> uses one seam over.
/// </para>
/// </summary>
sealed class AnnouncementPrivacyFlipEventSink(
    ChannelWriter<AnnouncementPrivacyFlipSignal> queue,
    IOptionsMonitor<StationOptions> stationMonitor,
    ILogger<AnnouncementPrivacyFlipEventSink> logger) : IStationEventSink
{
    internal const string SpectatorModeKey = "Station:SpectatorMode";

    public void Publish(StationEvent evt)
    {
        if (evt is not SettingChanged { Key: SpectatorModeKey }) return;
        if (!stationMonitor.CurrentValue.SpectatorMode) return; // only the private -> public direction ever declines anything (SPEC F145.2)

        if (!queue.TryWrite(new AnnouncementPrivacyFlipSignal()))
            logger.LogWarning("Announcement privacy-flip queue full — this write's own decline sweep was dropped");
    }
}
