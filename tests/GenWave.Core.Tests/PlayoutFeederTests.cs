using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests;

public class PlayoutFeederTests
{
    // A measured item; its loudness sits at target so its gain is ~0 (irrelevant to these tests).
    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    // Default rotation window (SPEC F41.6) matching the retired recentCapacity ctor default — these
    // tests are not exercising the live-window seam (that's Story134_FeederRecentWindowLive.cs), so
    // a fresh 20/2-default provider per feeder keeps their pre-F41.6 behavior unchanged.
    static IRotationSettingsProvider DefaultRotation() => new FakeRotationSettingsProvider(new RotationSettings());

    [Fact]
    public async Task RealTrackAiringAtBoot_RefillsImmediately()
    {
        // A track_id airing at boot proves NOTHING about the queue: since Epic K (F21.4) safe
        // tracks carry track_ids too, so the airing item may be a drain the engine is covering
        // with an empty queue behind it. The feeder must refill on the boot tick — the cost when
        // the airing item WAS a real pushed track (api restart mid-chain) is one extra queued
        // chain, which is bounded and silent-safe; the cost of trusting was an unrecoverable
        // dead-air deadlock whenever the safe rotation cycles a single id (live, 2026-07-12).
        // Amends SPEC F7.5.
        var ls = new FakeLiquidsoapControl(["A"], Real("A"));
        var provider = new FakeNextItemProvider(Item("m1"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);

        var pushed = Assert.Single(ls.Pushed);
        Assert.Equal("m1", pushed.MediaId);
    }

    [Fact]
    public async Task SingleSegmentSafeRotationAtBoot_DoesNotDeadlock()
    {
        // The exact 2026-07-12 live deadlock: boot while an ANNOTATED safe track airs, and the
        // safe rotation holds only ONE playable segment, so the on-air id never changes and no
        // foreign-id advance can ever correct a misplaced boot trust. The feeder must keep
        // consulting the provider every tick until a push lands — never freeze at prepared=1.
        var ls = new FakeLiquidsoapControl(["9087", "9087", "9087"], Real("9087"));
        var provider = new FakeNextItemProvider(null, null, Item("m1"));  // selection recovers on tick 3
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);
        await feeder.TickAsync(CancellationToken.None);
        await feeder.TickAsync(CancellationToken.None);

        var pushed = Assert.Single(ls.Pushed);
        Assert.Equal("m1", pushed.MediaId);
        Assert.Equal(3, provider.Calls.Count);   // consulted every tick — no frozen prepared=1
    }

    [Fact]
    public async Task SafeRotationAiring_RefillsViaProvider()
    {
        // The safe rotation is airing (its on-air id carries no media id) ⇒ the queue drained. The
        // feeder detects the missing stamped id and self-heals by pulling + pushing.
        var ls = new FakeLiquidsoapControl(["safe"], Real());
        var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(Item("m1")), DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);

        var pushed = Assert.Single(ls.Pushed);
        Assert.Equal("m1", pushed.MediaId);
    }

    [Fact]
    public async Task Advance_PreparesNext_DoesNotRepushAdvancedTrack()
    {
        // Boot airing A (B prepared). Then advance to B: the feeder prepares the NEXT (C) and must not
        // re-push B, the track it just advanced onto.
        var ls = new FakeLiquidsoapControl(["A", "B"], Real("A", "B"));
        var provider = new FakeNextItemProvider(Item("C"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // boot at A → refills (pushes C)
        await feeder.TickAsync(CancellationToken.None);   // advance A→B: B is foreign → refill retries, provider exhausted → no re-push of B

        var pushed = Assert.Single(ls.Pushed);
        Assert.Equal("C", pushed.MediaId);
    }

    [Fact]
    public async Task ProviderReturnsNull_RetriesNextTick_NoStall()
    {
        // Tolerant pull: a null (cold/empty library) is non-fatal — the feeder simply retries next tick.
        var ls = new FakeLiquidsoapControl(["safe", "safe"], Real());
        var provider = new FakeNextItemProvider(null, Item("m1"));   // 1st tick null, 2nd tick a track
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // drained, provider null → no push
        Assert.Empty(ls.Pushed);

        await feeder.TickAsync(CancellationToken.None);   // still drained, provider returns → push
        Assert.Single(ls.Pushed);
        Assert.Equal(2, provider.Calls.Count);            // retried, did not stall
    }

    // ── §0 BLOCKING acceptance (PRD §12) — the on-air read is the OUTPUT metadata: the on-air id is our
    //    stamped media id, or absent when the safe rotation airs. Drain and advance are driven by that
    //    signal, NEVER by RID arithmetic. ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Feeder_DrainToSafe_DetectsViaMissingStampedId_AndRefills()
    {
        // A real track is airing (one prepared). Then the queue empties and `safe` airs: the output
        // metadata carries NO stamped id. The feeder MUST read that absence as a drained queue and
        // refill — the exact case the request.all−queue workaround could miss when a stale RID lingered.
        var ls = new FakeLiquidsoapControl(["100", "no-stamped-id"], Real("100"));
        var provider = new FakeNextItemProvider(Item("boot-refill"), Item("refill"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // boot → refills immediately (F7.5 as amended 2026-07-12)
        Assert.Single(ls.Pushed);

        await feeder.TickAsync(CancellationToken.None);   // safe airing ⇒ missing stamped id ⇒ refill again
        Assert.Equal(2, ls.Pushed.Count);
        Assert.Equal("refill", ls.Pushed[^1].MediaId);
    }

    [Fact]
    public async Task Drained_PushLost_RetriesEveryTick()
    {
        // Deadlock regression (PRD §6.3): the on-air drain token persists even after a push — meaning
        // the push never aired. Without the fix, prepared stays 1 after the first tick and the feeder
        // never pushes again. With the fix, safe-rotation airing resets prepared=0 each tick so the
        // feeder retries. Three ticks ⇒ three pushes.
        var ls = new FakeLiquidsoapControl(["safe", "safe", "safe"], Real());
        var provider = new FakeNextItemProvider(Item("m1"), Item("m2"), Item("m3"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // drain detected → push m1
        await feeder.TickAsync(CancellationToken.None);   // drain still airs (m1 lost) → push m2
        await feeder.TickAsync(CancellationToken.None);   // drain still airs (m2 lost) → push m3

        Assert.Equal(3, ls.Pushed.Count);
        Assert.Equal("m1", ls.Pushed[0].MediaId);
        Assert.Equal("m2", ls.Pushed[1].MediaId);
        Assert.Equal("m3", ls.Pushed[2].MediaId);
    }

    [Fact]
    public async Task Drained_PushAirs_StopsRefilling()
    {
        // Once a pushed item actually airs (its stamped id appears as on-air) the feeder must NOT
        // keep pushing — the one-ahead invariant is satisfied and a duplicate push would double-queue.
        var ls = new FakeLiquidsoapControl(["safe", "m1", "m1"], Real("m1"));
        var provider = new FakeNextItemProvider(Item("m1"), Item("m2"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // drain → push m1
        await feeder.TickAsync(CancellationToken.None);   // m1 now airs → advance, prepared=0 → push m2
        await feeder.TickAsync(CancellationToken.None);   // m1 still on air (no change) → no push

        Assert.Equal(2, ls.Pushed.Count);
        Assert.Equal("m1", ls.Pushed[0].MediaId);
        Assert.Equal("m2", ls.Pushed[1].MediaId);
    }

    // ── gitea-#155 — TTS segments air for seconds (shorter than the tick), so the feeder must push the
    //    whole chain through to the next MUSIC track in one refill; mid-chain advances must not
    //    trigger a duplicate refill while the rest of the chain is still queued. ─────────────────

    [Fact]
    public async Task Refill_TtsChain_PushesThroughToMusicInOneTick()
    {
        // Drained. The provider yields back-announce, lead-in, then music: one tick must push ALL
        // three — stopping a TTS segment one-ahead would let the queue drain between segments.
        var ls = new FakeLiquidsoapControl(["safe"], Real());
        var provider = new FakeNextItemProvider(Item("tts:ba"), Item("tts:lead"), Item("m1"), Item("tts:never"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);

        Assert.Equal(["tts:ba", "tts:lead", "m1"], ls.Pushed.Select(p => p.MediaId));
    }

    [Fact]
    public async Task MidChainTtsAdvance_DoesNotRefill_MusicAdvanceDoes()
    {
        // Chain [tts:a, m1] pushed on drain. tts:a airing is a MID-chain advance (m1 still queued):
        // no refill. m1 airing is the chain END: refill with the next chain.
        var ls = new FakeLiquidsoapControl(["safe", "tts:a", "m1"], Real("tts:a", "m1"));
        var provider = new FakeNextItemProvider(Item("tts:a"), Item("m1"), Item("tts:b"), Item("m2"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // drain → push chain [tts:a, m1]
        Assert.Equal(2, ls.Pushed.Count);

        await feeder.TickAsync(CancellationToken.None);   // tts:a airs mid-chain → NO refill
        Assert.Equal(2, ls.Pushed.Count);

        await feeder.TickAsync(CancellationToken.None);   // m1 (chain end) airs → next chain
        Assert.Equal(["tts:a", "m1", "tts:b", "m2"], ls.Pushed.Select(p => p.MediaId));
    }

    [Fact]
    public async Task Refill_ProviderYieldsOnlyTts_StopsAtChainCap()
    {
        // A provider that never yields music must not let one tick push unbounded TTS.
        var onlyTts = Enumerable.Range(1, 20).Select(i => Item($"tts:{i}")).ToArray();
        var ls = new FakeLiquidsoapControl(["safe"], Real());
        var feeder = new PlayoutFeeder(ls, new FakeNextItemProvider(onlyTts), DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);

        Assert.Equal(8, ls.Pushed.Count);   // MaxChainLength
    }

    [Fact]
    public async Task Feeder_Advance_DetectedByStampedIdChange_NotRid()
    {
        // Advancement fires on the stamped id CHANGING. The on-air ids here are deliberately non-numeric
        // ("alpha"→"beta") so no RID arithmetic is even possible: the feeder can only be keying on the
        // stamped-id change. On advance it prepares the next track and remembers what aired.
        var ls = new FakeLiquidsoapControl(["alpha", "beta"], Real("alpha", "beta"));
        var provider = new FakeNextItemProvider(Item("next"));
        var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

        await feeder.TickAsync(CancellationToken.None);   // boot at alpha → refills (pushes "next")
        await feeder.TickAsync(CancellationToken.None);   // stamped id changed alpha→beta → refill retries (provider exhausted)

        var pushed = Assert.Single(ls.Pushed);
        Assert.Equal("next", pushed.MediaId);
        // The recently-aired ids flow to the selection seam for repeat-avoidance. "next" is present
        // too — it joined the ring at push time (SPEC F57.3), before its own advance was observed.
        Assert.Equal(["alpha", "next", "beta"], provider.Calls[^1].RecentMediaIds);
    }
}
