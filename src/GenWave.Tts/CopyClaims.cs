using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// The mechanical claim checker (SPEC F138.1-F138.3, F144.3, gh-#434, gh-#438): pure and static, no
/// I/O, no settings reads, zero non-BCL dependencies beyond <see cref="ClaimVocabulary"/>'s own plain
/// data — the exact <see cref="SpeechText"/> purity posture (F68.6), named by F138.1 itself. Three
/// entry points: <see cref="CheckFacts"/> (F138.2, the context lane's "did the model invent a fact"),
/// <see cref="CheckClock"/> (F138.3, every patter kind's "did the model lie about the clock"), and —
/// as of PLAN T342 — <see cref="CheckContainment"/> (F144.3, the announcement lane's own "did the
/// model keep the owner's actual words", the one check of the three that looks for an ABSENCE rather
/// than an unsupported addition). All three are pure functions of their arguments — the re-ask/floor
/// ladder (F138.4/F144.4), the prompt guard line (F138.5), and every other stateful or config-reading
/// decision live at the call sites this checker is built for (PLAN T331/T332/T342), never here.
///
/// <para>
/// <b>False-positive posture (governs every ambiguous decision below):</b> when a match is uncertain,
/// this checker PASSES rather than rejects. A missed fabrication only ever airs a line no worse than
/// the pre-F138 status quo; a false rejection spends the F138.4 ladder's one re-ask on copy that was
/// actually fine, and can needlessly push good copy onto the deterministic template fallback. Every
/// heuristic here — whole-token digit support, word-boundary condition matching, present-frame-only
/// weekday/daypart extraction, title-substring exemption, "missing from the vocabulary" simply never
/// becoming a claim at all — is chosen with this bias, not tightened further even where a smarter rule
/// is possible.
/// </para>
///
/// <para>
/// <b>Input assumption — what <paramref name="copy"/> looks like when it reaches this checker:</b> the
/// intended caller (PLAN T331/T332) hands this <see cref="LlmCopyWriter"/>'s POST-hygiene,
/// PRE-<see cref="SpeechText"/> text — <c>ApplyCopyHygiene</c>'s output, or a <c>CleanCopy</c> sentence
/// salvage of it — never the raw model reply, and never the post-<see cref="SpeechText.Normalize"/>
/// speech form. Two matching choices below depend on this: case survives (so this checker matches
/// case-insensitively itself rather than trusting <see cref="SpeechText"/>'s later lowercase flatten to
/// have already happened), and punctuation/unit symbols are still literal (no F68 unit-expansion has
/// run yet, so a temperature still reads "21°C" rather than "21 degrees Celsius" — harmless either way,
/// since <see cref="DigitRunRx"/> only ever needs the leading digits).
/// </para>
/// </summary>
public static partial class CopyClaims
{
    /// <summary>
    /// SPEC F138.2 — every digit run, weekday name (present-frame only — see below), and weather
    /// condition word <paramref name="copy"/> claims must be supported by <paramref name="factBlock"/>
    /// — the segment's own RAW facts text (the same string the caller passes as
    /// <c>LlmPromptBuilder.BuildContextFactsLine</c>'s own <c>facts</c> parameter, BEFORE that method
    /// fences it with its "Use only these facts. Do not add facts." framing — <paramref name="factBlock"/>
    /// is never the already-fenced prompt line itself): a digit run must appear as a WHOLE TOKEN (see
    /// below); a weekday or condition word must appear as a WHOLE WORD, case-insensitively (see
    /// <see cref="ClaimVocabulary"/> for the exact vocabularies this draws from). Daypart words are not
    /// an F138.1 claim class and are never extracted here — they only ever matter against the clock
    /// (see <see cref="CheckClock"/>).
    ///
    /// <para>
    /// <b>Digit-run tokenization and support (amended T329 review round 1, the F135.5 precedent):</b> a
    /// digit run is a maximal <c>[0-9]+(?:\.[0-9]+)?</c> match — contiguous digits, plus one optional
    /// embedded decimal point (so "108.8" is ONE token, never split into "108" and "8"). A copy token
    /// is supported iff it is EQUAL to one of the fact block's own digit-run tokens, OR some fact-block
    /// token starts with the copy token followed by a literal "." (the deliberate decimal-prefix
    /// allowance kept explicitly: "108" is supported by a fact block carrying "108.8"). This replaces
    /// the original literal-substring reading, which made every short number unfalsifiable the moment a
    /// fact block carried a date or timestamp — "1" would have been "supported" by any fact block
    /// containing "14:37" purely because "1" is a substring of "14", never mind that "1" never appears
    /// as its OWN token anywhere. A hyphenated range ("12-15") tokenizes into its two ENDPOINTS ("12",
    /// "15") for free, since a hyphen is not a digit — so a range's own printed endpoints support
    /// themselves, but a value strictly BETWEEN them (e.g. copy claims "13" against a fact reading
    /// "12-15") is not equal to either endpoint and is reported as unsupported — a known, accepted
    /// conservative gap (full numeric-range interpolation was judged unjustified complexity for a
    /// fixed, narrow claim surface; see the false-positive-posture remarks above for why this is an
    /// acceptable place to lean the other way, toward flagging, given the re-ask cost is bounded to one
    /// retry). A second, symmetric, still-accepted gap: whole-token equality means a ROUNDED claim
    /// against a precise fact is flagged even when a person would call it correct — "21" against a fact
    /// block reading "20.6" is not equal to "20.6" and is not a "20.6"-style decimal PREFIX of it
    /// either, so it violates, even though "21" is simply "20.6" rounded. Left as-is rather than adding
    /// numeric-rounding logic: the false-positive posture accepts an occasional needless re-ask here
    /// over adding a second, fuzzier definition of "supported" alongside the decimal-prefix rule. A
    /// third, related gap: a LEADING ZERO is never stripped — "8" against a fact block reading
    /// "2026-08-08" violates, because the fact block's own digit-run token there is "08", and the
    /// <see cref="StringComparison.Ordinal"/> equality check above treats "8" and "08" as different
    /// tokens (an ordinal-string, not a numeric-value, comparison). Deliberate and conservative, the
    /// same posture as the other two gaps above: a date component spelled with its own leading zero
    /// is common in fact blocks (ISO dates) but rare in spoken copy ("the 8th", not "the 08th"), so
    /// this gap almost never fires on real copy, and when it does, flagging costs one re-ask rather
    /// than risking a false pass.
    /// </para>
    ///
    /// <para>
    /// <b>No track-title exemption here (deliberate, unlike <see cref="CheckClock"/>):</b> this method
    /// takes no <c>trackTitle</c> parameter, because no track-bearing copy reaches <see cref="CheckFacts"/>
    /// today — a <see cref="SegmentKind.ContextSegment"/> request's own <c>Track</c> is always null (see
    /// <c>LlmPromptBuilder.BuildUserContent</c>'s own T224 remarks and <c>Orchestrator.BuildContextSegmentRequestAsync</c>'s
    /// literal <see langword="null"/> at that position), and <see cref="CrosstalkExchangeRequest"/>
    /// carries no track either. <b>Trigger:</b> the day any future <see cref="SegmentKind"/> hands
    /// track-bearing copy through this same fact-checking path, this exemption gap needs revisiting —
    /// a title like "Purple Rain" or "Snow Patrol" would otherwise trip the condition-word class the
    /// same way it can already trip <see cref="CheckClock"/>'s weekday/daypart classes without the
    /// exemption <see cref="CheckClock"/> carries.
    /// </para>
    /// </summary>
    public static ClaimCheckResult CheckFacts(string copy, string factBlock)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(factBlock);

        var violations = new List<ClaimViolation>();
        var factDigitTokens = DigitRunRx().Matches(factBlock).Select(match => match.Value).ToArray();

        foreach (var token in DistinctTokens(DigitRunRx().Matches(copy)))
        {
            var supported = factDigitTokens.Any(fact =>
                string.Equals(fact, token, StringComparison.Ordinal) ||
                fact.StartsWith(token + ".", StringComparison.Ordinal));
            if (!supported)
                violations.Add(new ClaimViolation(ClaimClass.DigitRun, token));
        }

        foreach (var (token, _, _) in DistinctPresentFrameWeekdays(copy, exemptSpans: []))
        {
            if (!ContainsWord(factBlock, token))
                violations.Add(new ClaimViolation(ClaimClass.Weekday, token));
        }

        foreach (var token in DistinctTokens(ConditionWordRx().Matches(copy)))
        {
            if (!ContainsWord(factBlock, token))
                violations.Add(new ClaimViolation(ClaimClass.ConditionWord, token));
        }

        return new ClaimCheckResult(violations);
    }

    /// <summary>
    /// SPEC F138.3 — every weekday/daypart claim in <paramref name="copy"/> must match the clock this
    /// break was actually written against: <paramref name="stationLocalNow"/>, the SAME instant
    /// <c>LlmPromptBuilder.BuildStationClockLine</c> renders into the prompt (amended T329 review round
    /// 1 — one <see cref="DateTimeOffset"/> parameter, not a separately-computed weekday/hour pair, so
    /// prompt and check provably read the same instant and <see cref="ClaimVocabulary.CategoryForHour"/>'s
    /// hour-range validation is unreachable from here: <see cref="DateTimeOffset.Hour"/> is always
    /// 0-23 by construction). A named weekday that doesn't match <paramref name="stationLocalNow"/>'s
    /// own <see cref="DateTimeOffset.DayOfWeek"/>, or a daypart word whose category (see
    /// <see cref="ClaimVocabulary.CategoryOf"/>) has no window covering <paramref name="stationLocalNow"/>'s
    /// own <see cref="DateTimeOffset.Hour"/> (see <see cref="ClaimVocabulary.HourIsInCategory"/>), is a
    /// violation carrying the correct value in <see cref="ClaimViolation.Expected"/>. Applies uniformly
    /// to every LLM patter kind (F138.3's own "all patter kinds" — this method does not know or care
    /// which kind called it; that gate lives at the T332 call site).
    ///
    /// <para>
    /// <b>Present-frame-only extraction (amended T329 review round 1 — read literally, the original
    /// letter rejected the bread of DJ patter: anticipation "join us next Friday", recall "last
    /// Saturday's show", "coming up tonight" said from a morning hour):</b> a weekday or daypart word is
    /// a CLOCK CLAIM at all only when it is asserted as the present frame, under a small closed set of
    /// markers immediately preceding it. Weekdays: "this {weekday}", "today is {weekday}", "it is
    /// {weekday}"/"it's {weekday}", "happy {weekday}" — and, structurally for free (nothing here anchors
    /// on what follows the weekday), "{weekday} {daypart}" whenever the weekday itself is one of those
    /// four, e.g. "this Saturday morning". Dayparts: greeting/copula only — "good {daypart}", "it is
    /// {daypart}"/"it's {daypart}" — deliberately NOT "this {daypart}" ("we opened this morning" said at
    /// night is recall, not a lie about tonight). Daypart windows OVERLAP rather than partition (see
    /// <see cref="ClaimVocabulary.HourIsInCategory"/>): a word passes if the hour falls in ANY window its
    /// own category names — "Good evening" at 21:00 is not a lie just because 21:00 is also "night".
    /// Everything else — last/next/every/a/on-a/tomorrow/yesterday {weekday}, a possessive
    /// {weekday}'s, a plural {weekday}s, or a bare mention with no marker at all — is displaced or
    /// generic reference, never extracted as a claim (the false-positive posture: when in doubt, pass).
    /// Both gh-#438 aired exhibits still violate under this narrowed rule.
    /// </para>
    ///
    /// <para>
    /// <b>Track-title exemption (F138.3):</b> <paramref name="trackTitle"/> excludes any weekday/daypart
    /// claim whose OWN matched word falls entirely inside a literal (case-insensitive) occurrence of the
    /// title text somewhere in <paramref name="copy"/> — "Saturday Night Fever" mentioned by name never
    /// trips the gate, including a present-frame-marked mention like "it's Saturday Night Fever".
    /// Documented limit: only an EXACT, literal (whole, contiguous) mention of the title text is exempt;
    /// a paraphrase or a partial quote of it gets no exemption, because a checker this simple has no way
    /// to distinguish a title reference from a genuine claim other than the title's own literal text
    /// appearing verbatim.
    /// </para>
    ///
    /// <para>
    /// <b>Owner-message exemption (HIGH-2 review finding, SPEC F144.3's own binding owner-trust rule):
    /// </b> <paramref name="ownerMessage"/> — <see cref="LlmCopyWriter.WriteAnnouncementAsync"/>'s own
    /// <c>message</c>, passed through from <see cref="LlmCopyWriter.CheckTruthGate"/>'s
    /// <c>requiredCore</c> — is a SECOND exemption source, span-based exactly like
    /// <paramref name="trackTitle"/> above (the same <see cref="FindTitleSpans"/>/<see cref="IsExempt"/>
    /// mechanism, reused rather than duplicated): a weekday/daypart claim whose own matched word falls
    /// entirely inside a literal occurrence of the owner's own message is never even asked to match the
    /// station clock, because the message wrote those words, not the model. Without this, CheckClock was
    /// rejecting the owner's OWN present-frame words ("Bake sale this Saturday", "Happy Friday
    /// everyone") on every day but the one they happened to name — while the F144.4 verbatim fallback
    /// airs the identical words unchecked regardless, so the gate was only ever punishing the FLAVORED
    /// path for repeating what the unflavored path already airs freely. Span-based, exactly like the
    /// title exemption, is what keeps this narrow: a claim the model ADDS outside any literal quote of
    /// the message — "It's Saturday" inserted where the message never said so — still rejects, since
    /// <see cref="FindTitleSpans"/> only ever exempts a claim that falls INSIDE the message's own
    /// literal text, never the copy at large. <b>This holds even when the model's added claim names the
    /// SAME weekday the message already does</b> (HIGH-A review finding, fixed): the exempt occurrence
    /// inside the message must never consume <see cref="DistinctPresentFrameWeekdays"/>'s own dedupe
    /// slot for that weekday, or it would silently swallow the model's own later, genuine, non-exempt
    /// occurrence of the identical word — see that method's own remarks for the fix (exemption filters
    /// out BEFORE dedupe, never after).
    /// </para>
    ///
    /// <para>
    /// <b>Daypart violations dedupe by CATEGORY, not raw word</b> (review finding): "tonight" and
    /// "night" are the SAME claim (<see cref="ClaimVocabulary.CategoryOf"/>), so a line naming both
    /// reports at most one daypart violation, keyed on the first-seen spelling — never two violations
    /// for what is, to a listener, one lie about the same instant.
    /// </para>
    /// </summary>
    public static ClaimCheckResult CheckClock(
        string copy, DateTimeOffset stationLocalNow, string? trackTitle = null, string? ownerMessage = null)
    {
        ArgumentNullException.ThrowIfNull(copy);

        var exemptSpans = FindTitleSpans(copy, trackTitle);
        exemptSpans.AddRange(FindTitleSpans(copy, ownerMessage));
        var violations = new List<ClaimViolation>();
        var expectedWeekday = stationLocalNow.DayOfWeek.ToString();
        var clockHour = stationLocalNow.Hour;

        foreach (var (token, _, _) in DistinctPresentFrameWeekdays(copy, exemptSpans))
        {
            if (!string.Equals(token, expectedWeekday, StringComparison.OrdinalIgnoreCase))
                violations.Add(new ClaimViolation(ClaimClass.Weekday, token, expectedWeekday));
        }

        foreach (var (token, _, category) in DistinctPresentFrameDayparts(copy, exemptSpans))
        {
            if (!ClaimVocabulary.HourIsInCategory(category, clockHour))
                violations.Add(new ClaimViolation(ClaimClass.Daypart, token, ClaimVocabulary.CategoryForHour(clockHour)));
        }

        return new ClaimCheckResult(violations);
    }

    /// <summary>
    /// SPEC F144.3 (STORY-358, PLAN T342) — the announcement-core containment check: the OPPOSITE
    /// direction from <see cref="CheckFacts"/>/<see cref="CheckClock"/> above (T329's own pure-checker
    /// posture, extended rather than reused wholesale). Those two extract a claim FROM
    /// <paramref name="copy"/> and ask whether something else supports it; this one asks whether
    /// <paramref name="requiredCore"/> — the owner's own announcement text, injected upstream as the
    /// F144.3 owner-trusted fact — survives INSIDE <paramref name="copy"/> at all. A single violation
    /// (never more than one — there is exactly one core to check, not a set of extracted claims) when
    /// it does not.
    ///
    /// <para>
    /// <b>Both sides normalized IDENTICALLY before the substring test (HIGH-1 review finding — the
    /// mismatched-lanes bug):</b> <paramref name="copy"/> arrives POST-<see cref="LlmCopyWriter.ApplyCopyHygiene"/>
    /// (this class's own input-assumption remarks above), but <paramref name="requiredCore"/> is the
    /// RAW owner message, never hygiene-shaped. Left unreconciled, any message carrying an internal
    /// double space, an embedded newline, markdown emphasis, or a bracketed aside was structurally
    /// unsatisfiable — hygiene had already collapsed/stripped every one of those shapes out of
    /// <paramref name="copy"/> by the time it reaches here, but <paramref name="requiredCore"/> still
    /// carried them raw, so an otherwise byte-for-byte echo could never pass the substring test.
    /// <paramref name="requiredCore"/> is therefore run through the SAME
    /// <see cref="LlmCopyWriter.ApplyCopyHygiene"/> pass first — that method is internal precisely for
    /// a second caller (<c>CrosstalkScriptParser.cs</c>'s own reuse is the existing precedent), never a
    /// second, hand-maintained hygiene pass here. The curly-vs-straight apostrophe class rides the
    /// identical fold-both-sides discipline (SPEC F144.3, the F68.4 survival precedent — F68.4 folds
    /// BOTH sides of its own comparison, never trusting either one to already match the other's form):
    /// <see cref="LlmCopyWriter.FoldApostrophes"/> runs on <paramref name="copy"/> AND the
    /// hygiene-shaped <paramref name="requiredCore"/> alike, so a straight apostrophe in the owner's
    /// message and a curly one in the model's reply (or the inverse) never falsely trips this check —
    /// the same standing both-forms discipline <see cref="PresentFrameWeekdayRx"/>'s own comment
    /// documents for this exact glyph pair, one claim class over.
    /// </para>
    ///
    /// <para>
    /// <b>Case-folded, not byte-identical (SPEC F144.3, the F68.4 survival precedent):</b>
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> — the DJ persona is free to flavor delivery
    /// AROUND the message (a framing line before or after), but the message's own wording, once
    /// spoken, must be recognizable letter-for-letter modulo case, exactly the discipline F68.4
    /// already established for a stylized name's own marks. No fuzzy/paraphrase matching is
    /// attempted, deliberately: a pure substring test is mechanically checkable and gives the F138.4
    /// ladder's one re-ask something concrete to fix (say the message, word for word), the same
    /// bias-toward-false-positive-over-false-negative posture <see cref="CheckFacts"/>'s own class
    /// remarks describe — here inverted to "when in doubt, still ask for the exact words".
    /// </para>
    ///
    /// <para>
    /// <see cref="ClaimViolation.Token"/> on the single violation this can produce is a FIXED,
    /// compile-time literal ("the announcement message"), never a fragment of
    /// <paramref name="requiredCore"/> itself — unlike <see cref="CheckFacts"/>/<see cref="CheckClock"/>'s
    /// own closed-vocabulary-or-digit-shaped tokens, <paramref name="requiredCore"/> is 280 chars of
    /// owner-authored free text (SPEC F143.4) with no vocabulary guarantee at all, so nothing of it is
    /// safe to splice into a future re-ask prompt line the way those tokens are (see
    /// <see cref="ClaimViolation.Token"/>'s own remarks) — the re-ask line names the REQUIREMENT, not
    /// the missing text, and the message itself still rides the SAME user prompt the re-ask appends
    /// to, so the model never loses sight of it.
    /// </para>
    /// </summary>
    public static ClaimCheckResult CheckContainment(string copy, string requiredCore)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(requiredCore);

        var normalizedCopy = LlmCopyWriter.FoldApostrophes(copy);
        var normalizedCore = LlmCopyWriter.FoldApostrophes(LlmCopyWriter.ApplyCopyHygiene(requiredCore));

        // MEDIUM-B review finding: an all-markup message ("*urgent*", "[Reminder]") hygiene-strips to
        // an EMPTY normalizedCore, and string.Contains("") is vacuously true for ANY copy — the gate
        // would silently pass whatever the model wrote, and the owner's own message would never reach
        // air at all (no violation to ride the ladder, so no F144.4 verbatim floor either). Treat an
        // empty (or whitespace-only) normalized core as a violation instead: it rides the SAME re-ask
        // ladder every other containment miss does, and once that ladder exhausts, the caller's F144.4
        // fallback still airs the RAW (pre-hygiene) message verbatim — the honest outcome, never a
        // silent no-op.
        if (string.IsNullOrWhiteSpace(normalizedCore))
            return new ClaimCheckResult([new ClaimViolation(ClaimClass.AnnouncementCore, AnnouncementCoreToken)]);

        return normalizedCopy.Contains(normalizedCore, StringComparison.OrdinalIgnoreCase)
            ? new ClaimCheckResult([])
            : new ClaimCheckResult([new ClaimViolation(ClaimClass.AnnouncementCore, AnnouncementCoreToken)]);
    }

    /// <summary>The fixed, safe-to-interpolate <see cref="ClaimViolation.Token"/> every
    /// <see cref="CheckContainment"/> violation carries — see that method's own remarks for why this
    /// is a literal, never a fragment of the checked message.</summary>
    const string AnnouncementCoreToken = "the announcement message";

    /// <summary>
    /// Every distinct (case-insensitive, first-occurrence-casing-wins) present-frame weekday claim in
    /// <paramref name="copy"/> — see <see cref="CheckClock"/>'s own remarks for the exact marker set —
    /// as (token, character index, character length) triples, the span covering only the WEEKDAY word
    /// itself, never its marker, so a title-exemption or dedup check tests the claim's own span.
    /// Excludes any match <paramref name="exemptSpans"/> covers (the track-title/owner-message
    /// exemptions) — mirrors <see cref="DistinctPresentFrameDayparts"/>'s own <c>titleSpans</c>
    /// parameter exactly.
    ///
    /// <para>
    /// <b>Exemption is filtered BEFORE the dedupe <c>seen</c> add (HIGH-A review finding, the
    /// dedupe-slot leak):</b> an EXEMPT occurrence — e.g. the owner's own message quoting "this
    /// Saturday" — must never consume the one dedupe slot for "Saturday" and so silently suppress a
    /// LATER, genuine, non-exempt occurrence of the same weekday word. <see cref="CheckClock"/>'s own
    /// caller relies on this ordering: an owner message that itself names a weekday, echoed by the
    /// model plus its OWN separate present-frame claim of the same weekday elsewhere in the copy, must
    /// still report that second claim as a violation — filtering exempt matches out first, then
    /// deduping only what remains, is what keeps that true.
    /// </para>
    /// </summary>
    static IEnumerable<(string Token, int Index, int Length)> DistinctPresentFrameWeekdays(
        string copy, List<(int Start, int End)> exemptSpans)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PresentFrameWeekdayRx().Matches(copy))
        {
            var group = match.Groups["weekday"];
            if (IsExempt(group.Index, group.Length, exemptSpans))
                continue;

            if (seen.Add(group.Value))
                yield return (group.Value, group.Index, group.Length);
        }
    }

    /// <summary>
    /// Every distinct present-frame daypart claim in <paramref name="copy"/> — see
    /// <see cref="CheckClock"/>'s own remarks for the exact marker set — as (token, character index,
    /// daypart category) triples, deduped by CATEGORY (not raw word, so "tonight" and "night" collapse
    /// into the one claim) and excluding any match the track-title exemption covers.
    /// </summary>
    static IEnumerable<(string Token, int Index, string Category)> DistinctPresentFrameDayparts(
        string copy, List<(int Start, int End)> titleSpans)
    {
        var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PresentFrameDaypartRx().Matches(copy))
        {
            var group = match.Groups["daypart"];
            if (IsExempt(group.Index, group.Length, titleSpans))
                continue;

            var category = ClaimVocabulary.CategoryOf(group.Value);
            if (seenCategories.Add(category))
                yield return (group.Value, group.Index, category);
        }
    }

    /// <summary>
    /// Every LITERAL (case-insensitive) occurrence of <paramref name="trackTitle"/> inside
    /// <paramref name="copy"/>, as start/end character spans — the exemption zones
    /// <see cref="IsExempt"/> checks a match against. Empty when <paramref name="trackTitle"/> is null,
    /// blank, or never mentioned verbatim.
    /// </summary>
    static List<(int Start, int End)> FindTitleSpans(string copy, string? trackTitle)
    {
        var spans = new List<(int Start, int End)>();
        if (string.IsNullOrEmpty(trackTitle))
            return spans;

        var searchFrom = 0;
        while (true)
        {
            var found = copy.IndexOf(trackTitle, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                break;

            spans.Add((found, found + trackTitle.Length));
            searchFrom = found + trackTitle.Length;   // advance past the whole match — a title mention
                                                       // cannot meaningfully overlap itself, and this
                                                       // avoids re-scanning inside a match already found
        }

        return spans;
    }

    /// <summary>True when a claim span [<paramref name="index"/>, <paramref name="index"/> + <paramref name="length"/>) falls entirely inside one of <paramref name="titleSpans"/> — the F138.3 track-title exemption.</summary>
    static bool IsExempt(int index, int length, List<(int Start, int End)> titleSpans) =>
        titleSpans.Exists(span => index >= span.Start && index + length <= span.End);

    /// <summary>
    /// The distinct (case-insensitive, first-occurrence-casing-wins) matched values of
    /// <paramref name="matches"/>, in the order first seen — collapses repeated mentions of the same
    /// claim ("sunny... sunny again") into a single reported violation rather than one per occurrence.
    /// </summary>
    static IEnumerable<string> DistinctTokens(IEnumerable<Match> matches)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (seen.Add(match.Value))
                yield return match.Value;
        }
    }

    /// <summary>
    /// Whole-word (letter/digit-boundary), case-insensitive search for <paramref name="word"/> inside
    /// <paramref name="haystack"/> — the F138.2 "by token" support rule for weekday/condition claims.
    /// A manual boundary check rather than a dynamically-built <see cref="Regex"/>: <paramref name="word"/>
    /// varies per call (it is the extracted claim, not a fixed pattern), so it cannot be a
    /// <c>[GeneratedRegex]</c> — those require a compile-time-constant pattern (see this file's own
    /// <see cref="PresentFrameWeekdayRx"/>/<see cref="ConditionWordRx"/>/<see cref="PresentFrameDaypartRx"/>
    /// for the fixed patterns that CAN be, and are).
    /// </summary>
    static bool ContainsWord(string haystack, string word)
    {
        var searchFrom = 0;
        while (true)
        {
            var found = haystack.IndexOf(word, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return false;

            var before = found == 0 || !char.IsLetterOrDigit(haystack[found - 1]);
            var afterIndex = found + word.Length;
            var after = afterIndex == haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (before && after)
                return true;

            searchFrom = found + 1;
        }
    }

    // Maximal digit run, with at most one embedded decimal point (see CheckFacts's own remarks for
    // the documented tokenization rule this implements — decimals stay one token, ranges tokenize
    // into their two endpoints for free). [0-9] rather than \d (review finding): explicit ASCII digit
    // class, not the Unicode-digit-aware shorthand — station facts/copy are always ASCII numerals.
    [GeneratedRegex(@"[0-9]+(?:\.[0-9]+)?")]
    private static partial Regex DigitRunRx();

    // Present-frame weekday marker (SPEC F138.3, amended T329 review round 1) — see CheckClock's own
    // remarks for the exact five-shape marker set this implements; the weekday itself is captured
    // separately from its marker (group "weekday") so a title-exemption/dedup check can test the
    // claim's own span. Interpolates ClaimVocabulary's own const alternation directly — a
    // compile-time-constant expression, so this stays source-generated.
    //
    // it[’']s (review round 3 fix): BOTH apostrophe forms — straight U+0027 and curly U+2019
    // (RIGHT SINGLE QUOTATION MARK) — mark "it's", never only the ASCII one. SpeechText's own
    // curly->straight fold (SpeechText.cs, ApplyCopyHygiene->Normalize) runs AFTER this checker by
    // design (this class's own remarks: the checker sees POST-hygiene, PRE-Normalize text), and
    // LlmCopyWriter already treats U+2019 as an apostrophe in three other places (SentenceBoundaryPattern,
    // the "here's" probe, IsApostrophe), so a model emitting "It’s Saturday" in exactly this
    // window is the expected case, not an edge one. The pattern below spells the curly form as
    // the \u2019 regex escape, not the raw glyph, to keep this source file itself ASCII — the next
    // edit here must keep BOTH forms, not silently narrow back to one.
    [GeneratedRegex(
        $@"\b(?:this|happy|today\s+is|it\s+is|it[\u2019']s)\s+(?<weekday>{ClaimVocabulary.WeekdayAlternation})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PresentFrameWeekdayRx();

    // Present-frame daypart marker (SPEC F138.3, amended) — greeting/copula only; deliberately NOT
    // "this {daypart}" (see CheckClock's own remarks for why). Captures the daypart word alone (group
    // "daypart"), same reasoning as the weekday marker above — including the same it[’']s
    // both-apostrophe-forms fix (see PresentFrameWeekdayRx's own remarks for why it must stay both).
    [GeneratedRegex(
        $@"\b(?:good|it\s+is|it[\u2019']s)\s+(?<daypart>{ClaimVocabulary.DaypartWordAlternation})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PresentFrameDaypartRx();

    // Internal, not private (PLAN T333 review advisory A1): CrosstalkScriptParser's own F138.6
    // weather-condition check reuses this SAME compiled pattern rather than keeping a byte-identical
    // copy that could silently drift the day either one changes — the one-canonical-source discipline
    // ClaimVocabulary.ConditionWordAlternation already establishes one level up, extended to the
    // compiled regex built from it.
    [GeneratedRegex($@"\b(?:{ClaimVocabulary.ConditionWordAlternation})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex ConditionWordRx();
}
