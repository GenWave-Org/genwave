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
    ///
    /// <para>
    /// <b>The station's own name is a supported fact everywhere it appears (SPEC F138.8, STORY-364,
    /// PLAN T350, gh-#632):</b> <paramref name="stationName"/> — <c>SegmentRequest.StationName</c>,
    /// threaded through from <see cref="LlmCopyWriter.CheckTruthGate"/> — joins the SAME span-exemption
    /// mechanism <see cref="CheckClock"/>'s own <c>trackTitle</c>/<c>ownerMessage</c> parameters already
    /// use (<see cref="FindTitleSpans"/>/<see cref="IsExempt"/>, never a second exemption mechanism): a
    /// digit-run token, or a present-frame weekday word, that falls entirely inside a literal
    /// (case-insensitive) occurrence of the station's own name never becomes a claim at all — a station
    /// named "GWAV 108.8" saying its own call letters reads as the DJ naming the station, never as an
    /// invented "108.8" the fact block happens not to carry. An empty, blank, or null
    /// <paramref name="stationName"/> exempts nothing (<see cref="FindTitleSpans"/>'s own blank guard).
    /// A digit or weekday elsewhere in the copy, matching no part of the name, is still checked exactly
    /// as before — the exemption is span-based, never a blanket pass for the whole render.
    /// </para>
    /// </summary>
    public static ClaimCheckResult CheckFacts(string copy, string factBlock, string? stationName = null)
    {
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(factBlock);

        var nameSpans = FindTitleSpans(copy, stationName);
        var violations = new List<ClaimViolation>();
        var factDigitTokens = DigitRunRx().Matches(factBlock).Select(match => match.Value).ToArray();

        foreach (var token in DistinctNonExemptTokens(DigitRunRx().Matches(copy), nameSpans))
        {
            var supported = factDigitTokens.Any(fact =>
                string.Equals(fact, token, StringComparison.Ordinal) ||
                fact.StartsWith(token + ".", StringComparison.Ordinal));
            if (!supported)
                violations.Add(new ClaimViolation(ClaimClass.DigitRun, token));
        }

        foreach (var token in DistinctPresentFrameWeekdays(copy, nameSpans))
        {
            if (!ContainsWord(factBlock, token))
                violations.Add(new ClaimViolation(ClaimClass.Weekday, token));
        }

        // HIGH-1 review finding: this loop previously ran with NO exemption at all — a station named
        // "Sunny 101.5" tripped its own ConditionWord class on "Sunny", since the station-name span
        // was only ever threaded through the digit-run and weekday loops above. Now shares the SAME
        // DistinctNonExemptTokens helper those two use, rather than a third hand-rolled copy.
        foreach (var token in DistinctNonExemptTokens(ConditionWordRx().Matches(copy), nameSpans))
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
    ///
    /// <para>
    /// <b>Station-name exemption (SPEC F138.8, STORY-364, PLAN T350, gh-#632):</b> <paramref name="stationName"/>
    /// is a THIRD span source, alongside <paramref name="trackTitle"/> and <paramref name="ownerMessage"/>
    /// above — the SAME <see cref="FindTitleSpans"/>/<see cref="IsExempt"/> mechanism, never a fourth
    /// bespoke one. A weekday or daypart word falling entirely inside a literal occurrence of the
    /// station's own name is never a claim, on every lane that calls this method — the ordinary patter
    /// kinds, the announcement lane (via <see cref="LlmCopyWriter.CheckTruthGate"/>), and crosstalk (via
    /// <c>CrosstalkScriptParser.Parse</c>) alike. An empty, blank, or null <paramref name="stationName"/>
    /// exempts nothing.
    /// </para>
    /// </summary>
    public static ClaimCheckResult CheckClock(
        string copy, DateTimeOffset stationLocalNow, string? trackTitle = null, string? ownerMessage = null,
        string? stationName = null)
    {
        ArgumentNullException.ThrowIfNull(copy);

        var exemptSpans = FindTitleSpans(copy, trackTitle);
        exemptSpans.AddRange(FindTitleSpans(copy, ownerMessage));
        exemptSpans.AddRange(FindTitleSpans(copy, stationName));
        var violations = new List<ClaimViolation>();
        var expectedWeekday = stationLocalNow.DayOfWeek.ToString();
        var clockHour = stationLocalNow.Hour;

        foreach (var token in DistinctPresentFrameWeekdays(copy, exemptSpans))
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
    /// SPEC F144.3 (STORY-358, PLAN T342; amended STORY-365, PLAN T350, gh-#632) — the
    /// announcement-core containment check: the OPPOSITE direction from
    /// <see cref="CheckFacts"/>/<see cref="CheckClock"/> above (T329's own pure-checker posture,
    /// extended rather than reused wholesale). Those two extract a claim FROM <paramref name="copy"/>
    /// and ask whether something else supports it; this one asks whether <paramref name="requiredCore"/>
    /// — the owner's own announcement text, injected upstream as the F144.3 owner-trusted fact —
    /// survives INSIDE <paramref name="copy"/> at all. A single violation (never more than one — there
    /// is exactly one core to check, not a set of extracted claims) when it does not.
    ///
    /// <para>
    /// <b>"The core" is the message's WORD SEQUENCE, not its raw bytes (amended STORY-365, gh-#632):
    /// </b> both sides are reduced to WORD TOKENS after normalization (see <see cref="WordTokenRx"/> —
    /// a maximal run of Unicode letters/digits, with a single embedded apostrophe allowed mid-word so
    /// "it's" and "Mom's" stay one token each); anything that is not a letter or digit — a comma, a
    /// dash, an exclamation mark, an ellipsis, any punctuation at all — is never part of a token, only
    /// ever a separator between them. <paramref name="requiredCore"/>'s own token run must appear
    /// inside <paramref name="copy"/>'s token run CONTIGUOUS AND IN ORDER (see <see cref="ContainsWordRun"/>)
    /// — every core word present but reordered, or present with a word inserted between two that were
    /// adjacent in the message, still violates. Word comparison is
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> — case is free, exactly like punctuation. This
    /// replaces the ORIGINAL literal-substring reading, which rejected an otherwise verbatim echo over
    /// nothing more than a changed terminal punctuation mark or a capitalized word for emphasis ("hot."
    /// vs "HOT!") — a false reject exactly as costly as any other rung on the F138.4 ladder, for a
    /// difference no listener would ever call a broken promise. A PARAPHRASE — different words in
    /// roughly the same shape ("Dinner's ready and steamin' hot" for "Dinner is ready ... while it's
    /// hot") — still rejects: the word sequence itself is what must survive, not merely the gist.
    /// </para>
    ///
    /// <para>
    /// <b>Both sides normalized IDENTICALLY before tokenization (HIGH-1 review finding — the
    /// mismatched-lanes bug):</b> <paramref name="copy"/> arrives POST-<see cref="LlmCopyWriter.ApplyCopyHygiene"/>
    /// (this class's own input-assumption remarks above), but <paramref name="requiredCore"/> is the
    /// RAW owner message, never hygiene-shaped. Left unreconciled, any message carrying an internal
    /// double space, an embedded newline, markdown emphasis, or a bracketed aside was structurally
    /// unsatisfiable — hygiene had already collapsed/stripped every one of those shapes out of
    /// <paramref name="copy"/> by the time it reaches here, but <paramref name="requiredCore"/> still
    /// carried them raw. <paramref name="requiredCore"/> is therefore run through the SAME
    /// <see cref="LlmCopyWriter.ApplyCopyHygiene"/> pass first — that method is internal precisely for
    /// a second caller (<c>CrosstalkScriptParser.cs</c>'s own reuse is the existing precedent), never a
    /// second, hand-maintained hygiene pass here. The curly-vs-straight apostrophe class rides the
    /// identical fold-both-sides discipline (SPEC F144.3, the F68.4 survival precedent — F68.4 folds
    /// BOTH sides of its own comparison, never trusting either one to already match the other's form):
    /// <see cref="LlmCopyWriter.FoldApostrophes"/> runs on <paramref name="copy"/> AND the
    /// hygiene-shaped <paramref name="requiredCore"/> alike, BEFORE tokenization, so a straight
    /// apostrophe in the owner's message and a curly one in the model's reply (or the inverse) fold to
    /// the SAME word token rather than splitting into two different ones — the same standing
    /// both-forms discipline <see cref="PresentFrameWeekdayRx"/>'s own comment documents for this
    /// exact glyph pair, one claim class over.
    /// </para>
    ///
    /// <para>
    /// <b>An empty word sequence is a violation, never a vacuous pass (MEDIUM-B review finding,
    /// still standing under the amendment):</b> an all-markup message ("*urgent*", "[Reminder]")
    /// hygiene-strips to zero word tokens, and an empty needle would otherwise "contain" trivially in
    /// ANY copy — the gate would silently pass whatever the model wrote, and the owner's own message
    /// would never reach air at all (no violation to ride the ladder, so no F144.4 verbatim floor
    /// either). Treat a core that reduces to zero word tokens as a violation instead: it rides the
    /// SAME re-ask ladder every other containment miss does, and once that ladder exhausts, the
    /// caller's F144.4 fallback still airs the RAW (pre-hygiene) message verbatim — the honest
    /// outcome, never a silent no-op.
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

        var coreWords = WordTokenRx().Matches(normalizedCore).Select(match => match.Value).ToArray();

        // MEDIUM-B review finding, still standing under the STORY-365 word-sequence amendment: see
        // this method's own remarks above for why a core that reduces to zero words is a violation,
        // never a vacuous pass.
        if (coreWords.Length == 0)
            return new ClaimCheckResult([new ClaimViolation(ClaimClass.AnnouncementCore, AnnouncementCoreToken)]);

        var copyWords = WordTokenRx().Matches(normalizedCopy).Select(match => match.Value).ToArray();

        return ContainsWordRun(copyWords, coreWords)
            ? new ClaimCheckResult([])
            : new ClaimCheckResult([new ClaimViolation(ClaimClass.AnnouncementCore, AnnouncementCoreToken)]);
    }

    /// <summary>
    /// True when <paramref name="needle"/> appears inside <paramref name="haystack"/> as a CONTIGUOUS,
    /// IN-ORDER run — every needle word matches the haystack word at the same offset, for SOME starting
    /// offset — case-insensitively (<see cref="StringComparison.OrdinalIgnoreCase"/>, the same F68.4
    /// survival precedent <see cref="CheckContainment"/>'s own remarks describe). <paramref name="needle"/>
    /// is never empty by the time this runs (<see cref="CheckContainment"/>'s own zero-word guard
    /// returns before reaching here), so this method does not itself special-case that. A plain nested
    /// loop over at most a few hundred words (SPEC F143.4's 280-char message cap bounds both sides) —
    /// no need for a smarter substring algorithm at this scale.
    /// </summary>
    static bool ContainsWordRun(IReadOnlyList<string> haystack, IReadOnlyList<string> needle)
    {
        for (var start = 0; start <= haystack.Count - needle.Count; start++)
        {
            var matchesFromHere = true;
            for (var offset = 0; offset < needle.Count; offset++)
            {
                if (!string.Equals(haystack[start + offset], needle[offset], StringComparison.OrdinalIgnoreCase))
                {
                    matchesFromHere = false;
                    break;
                }
            }

            if (matchesFromHere)
                return true;
        }

        return false;
    }

    /// <summary>The fixed, safe-to-interpolate <see cref="ClaimViolation.Token"/> every
    /// <see cref="CheckContainment"/> violation carries — see that method's own remarks for why this
    /// is a literal, never a fragment of the checked message.</summary>
    const string AnnouncementCoreToken = "the announcement message";

    /// <summary>
    /// Every distinct (case-insensitive, first-occurrence-casing-wins), non-exempt present-frame
    /// weekday TOKEN in <paramref name="copy"/> — see <see cref="CheckClock"/>'s own remarks for the
    /// exact marker set. Delegates to <see cref="DistinctNonExemptTokens"/> against the WEEKDAY word's
    /// own captured group span, never its marker (so <paramref name="exemptSpans"/> — the track-title/
    /// owner-message/station-name exemptions — tests the claim's own span, not the marker text around
    /// it) — mirrors <see cref="DistinctPresentFrameDayparts"/>'s own <c>titleSpans</c> parameter
    /// exactly, one claim class over.
    ///
    /// <para>
    /// <b>Exemption is filtered BEFORE the dedupe add (HIGH-A review finding, the dedupe-slot leak,
    /// inherited from <see cref="DistinctNonExemptTokens"/>'s own ordering):</b> an EXEMPT occurrence —
    /// e.g. the owner's own message quoting "this Saturday" — must never consume the one dedupe slot
    /// for "Saturday" and so silently suppress a LATER, genuine, non-exempt occurrence of the same
    /// weekday word. <see cref="CheckClock"/>'s own caller relies on this ordering: an owner message
    /// that itself names a weekday, echoed by the model plus its OWN separate present-frame claim of
    /// the same weekday elsewhere in the copy, must still report that second claim as a violation.
    /// </para>
    /// </summary>
    static IEnumerable<string> DistinctPresentFrameWeekdays(string copy, List<(int Start, int End)> exemptSpans) =>
        DistinctNonExemptTokens(
            PresentFrameWeekdayRx().Matches(copy).Select(match => match.Groups["weekday"]), exemptSpans);

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
    ///
    /// <para>
    /// <b>The needle is trimmed and whitespace-collapsed before the search (MEDIUM-2 review finding,
    /// SPEC F138.8, STORY-364, PLAN T350, gh-#632):</b> <paramref name="copy"/> itself is already
    /// single-spaced by the time it reaches ANY caller of this method (this class's own input
    /// assumption remarks: <see cref="LlmCopyWriter.ApplyCopyHygiene"/>'s own whitespace collapse has
    /// already run), but a stored value — a Station:Name setting with a leading/trailing space, or a
    /// doubled internal space — is not guaranteed to arrive that clean. Left unreconciled, an untrimmed
    /// or double-spaced name silently exempted NOTHING: the raw needle never occurs verbatim inside the
    /// already-clean copy, so every span-exemption caller (<see cref="CheckFacts"/>'s and
    /// <see cref="CheckClock"/>'s own <c>stationName</c>) quietly went back to flagging the station's
    /// own name — the exact 2026-08-28 bug this re-guards. <see cref="CollapseWhitespace"/> is applied
    /// here, in this ONE place, since <paramref name="trackTitle"/>/<paramref name="ownerMessage"/>/
    /// <paramref name="stationName"/> — every span source <see cref="CheckClock"/> and
    /// <see cref="CheckFacts"/> carry — all funnel through this single method rather than each
    /// normalizing its own copy of the value beforehand.
    /// </para>
    /// </summary>
    static List<(int Start, int End)> FindTitleSpans(string copy, string? trackTitle)
    {
        var spans = new List<(int Start, int End)>();
        if (string.IsNullOrWhiteSpace(trackTitle))
            return spans;

        var needle = CollapseWhitespace(trackTitle);
        var searchFrom = 0;
        while (true)
        {
            var found = copy.IndexOf(needle, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                break;

            spans.Add((found, found + needle.Length));
            searchFrom = found + needle.Length;   // advance past the whole match — a title mention
                                                   // cannot meaningfully overlap itself, and this
                                                   // avoids re-scanning inside a match already found
        }

        return spans;
    }

    /// <summary>Trims and collapses internal whitespace runs to a single space (MEDIUM-2 review
    /// finding) — see <see cref="FindTitleSpans"/>'s own remarks for why its needle needs this before
    /// the search.</summary>
    static string CollapseWhitespace(string text) => WhitespaceRunRx().Replace(text.Trim(), " ");

    /// <summary>True when a claim span [<paramref name="index"/>, <paramref name="index"/> + <paramref name="length"/>) falls entirely inside one of <paramref name="titleSpans"/> — the F138.3 track-title exemption.</summary>
    static bool IsExempt(int index, int length, List<(int Start, int End)> titleSpans) =>
        titleSpans.Exists(span => index >= span.Start && index + length <= span.End);

    /// <summary>
    /// The first match of <paramref name="pattern"/> inside <paramref name="copy"/> whose OWN span does
    /// NOT fall entirely inside a literal (case-insensitive) occurrence of <paramref name="stationName"/>
    /// — the SAME <see cref="FindTitleSpans"/>/<see cref="IsExempt"/> station-name exemption mechanism
    /// <see cref="CheckFacts"/>'s and <see cref="CheckClock"/>'s own <c>stationName</c> parameter carry
    /// (SPEC F138.8, STORY-364, PLAN T350, gh-#632), reused here rather than re-implemented so
    /// <c>CrosstalkScriptParser.Parse</c>'s own <c>TruthShapeChecks</c> table (LOW-3 review finding) gets
    /// the identical "the station's own name is never a fabricated claim" exemption every other truth-gate
    /// lane already carries — never a parser-local copy of <see cref="FindTitleSpans"/>/<see cref="IsExempt"/>.
    /// <see langword="null"/> when every match in <paramref name="copy"/> is exempt, or there is no match
    /// at all. An empty, blank, or null <paramref name="stationName"/> exempts nothing (<see cref="FindTitleSpans"/>'s
    /// own blank guard).
    /// </summary>
    internal static Match? FirstNonExemptMatch(Regex pattern, string copy, string? stationName)
    {
        var nameSpans = FindTitleSpans(copy, stationName);
        foreach (Match match in pattern.Matches(copy))
        {
            if (!IsExempt(match.Index, match.Length, nameSpans))
                return match;
        }

        return null;
    }

    /// <summary>
    /// Every distinct (case-insensitive, first-occurrence-casing-wins), non-exempt matched value of
    /// <paramref name="claimSpans"/>, in the order first seen (HIGH-1 review finding) — the shared
    /// extraction shape the THREE VALUE-deduped claim classes (<see cref="CheckFacts"/>'s own
    /// digit-run, weekday, and condition-word loops) all reduce to, so a future value-deduped claim
    /// class inherits this same helper rather than a fourth hand-rolled Where(!IsExempt(...))-then-dedupe
    /// pair (L6 review finding, PLAN T350: the prior wording claimed "the ONE" extraction shape without
    /// scoping it, which overstated the count by one — <see cref="DistinctPresentFrameDayparts"/> is a
    /// FOURTH claim class already, and a justified exception to this helper: it dedupes by CATEGORY, not
    /// by the raw matched token, so "tonight" and "night" collapse into a single claim — a shape this
    /// helper cannot produce). Exemption is filtered out BEFORE the dedupe add (the same ordering
    /// <see cref="DistinctPresentFrameWeekdays"/>'s own remarks document one member up) — an exempt
    /// occurrence never consumes the one dedupe slot for a token that also occurs, genuinely, elsewhere
    /// in the copy. Collapses repeated mentions of the same claim ("sunny... sunny again") into a single
    /// reported violation rather than one per occurrence. Takes <see cref="Group"/> rather than
    /// <see cref="Match"/> so a caller needing a captured SUBGROUP's own span (not the whole match's —
    /// <see cref="DistinctPresentFrameWeekdays"/>'s own weekday-only span, never its marker) can select
    /// one; a plain <see cref="MatchCollection"/> flows in here for free, since <see cref="Match"/> is
    /// itself a <see cref="Group"/>.
    /// </summary>
    static IEnumerable<string> DistinctNonExemptTokens(
        IEnumerable<Group> claimSpans, List<(int Start, int End)> exemptSpans)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var span in claimSpans)
        {
            if (IsExempt(span.Index, span.Length, exemptSpans))
                continue;

            if (seen.Add(span.Value))
                yield return span.Value;
        }
    }

    /// <summary>
    /// Whole-word (letter/digit-boundary), case-insensitive search for <paramref name="word"/> inside
    /// <paramref name="haystack"/> — the F138.2 "by token" support rule for weekday/condition claims.
    /// A manual boundary check rather than a dynamically-built <see cref="Regex"/>: <paramref name="word"/>
    /// varies per call (it is the extracted claim, not a fixed pattern), so it cannot be a
    /// <c>[GeneratedRegex]</c> — those require a compile-time-constant pattern (see this file's own
    /// <see cref="PresentFrameWeekdayRx"/>/<see cref="ConditionWordRx"/>/<see cref="PresentFrameDaypartRx"/>
    /// for the fixed patterns that CAN be, and are). <c>char.IsLetterOrDigit</c>'s own Unicode-aware
    /// notion of "a word" AGREES with <see cref="WordTokenRx"/>'s <c>\p{L}</c> class on the LETTER side
    /// (LOW-4 review finding reconciled it there — this method never needed changing, since it was
    /// already Unicode-aware; <see cref="WordTokenRx"/> was the one that had drifted ASCII-only).
    ///
    /// <para>
    /// <b>It still DIFFERS from <see cref="WordTokenRx"/> on two axes, deliberately (L1 review finding,
    /// PLAN T350) — not a residual "two definitions of a word" bug:</b> (1) numerals —
    /// <c>char.IsDigit</c> covers only the decimal-digit category, narrower than <c>\p{N}</c>, so a
    /// character like "½", "²", or "Ⅷ" reads as a WORD BOUNDARY here but as part of a token under
    /// <see cref="WordTokenRx"/>; (2) apostrophes — this method treats <c>'</c> itself as a boundary
    /// character (so "Saturday" is found as a whole word inside "Saturday's"), while
    /// <see cref="WordTokenRx"/> keeps a mid-word apostrophe INSIDE its token ("it's" tokenizes as one
    /// word, not two, never as "it" then "s"). Both divergences are safe because this method's
    /// <paramref name="word"/> is never a free-typed string — it is always one fixed ASCII vocabulary
    /// word out of <see cref="ClaimVocabulary"/>'s weekday/condition lists, which contain no digits and
    /// no apostrophes to begin with. <see cref="WordTokenRx"/>'s broader Unicode-numeral and
    /// mid-word-apostrophe handling exists for a genuinely different input — the owner's free-typed
    /// announcement message (see its own remarks) — not for this method's fixed vocabulary.
    /// </para>
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

    // The F144.3-amended (STORY-365, gh-#632; widened HIGH-2/LOW-4 review round) word token: a maximal
    // run of Unicode letters/digits (\p{L}/\p{N} — \p{L} agrees with ContainsWord's own
    // char.IsLetterOrDigit boundary check on the LETTER side, LOW-4 review finding; \p{N} is
    // DELIBERATELY broader than that method's digit side, and the mid-word apostrophe below is
    // DELIBERATELY different from that method's own apostrophe-as-boundary rule — L1 review finding,
    // PLAN T350; see ContainsWord's own remarks for why both divergences are safe: its needle is
    // always a fixed ASCII vocabulary word, never a free-typed one), with a single embedded apostrophe
    // allowed mid-word so a contraction/possessive stays ONE token ("it's", "Mom's") rather than
    // splitting into two.
    // Replaces the review-round-1 ASCII-only [A-Za-z0-9] class: that class's own "station copy is
    // always ASCII" comment was WRONG for this method's real input — requiredCore is the OWNER's
    // free-typed announcement message (SPEC F143.4), not station copy, and carries no ASCII guarantee
    // at all. Under the ASCII-only class, a non-Latin message ("Καλημέρα everyone") tokenized to ZERO
    // words and hit the empty-core violation unconditionally regardless of what the copy said — an
    // outright regression against the ORIGINAL literal-substring check this method replaced — and an
    // accented Latin word ("Café", "über") split at its own accented letter, both rejecting a verbatim
    // echo (false reject) and, worse, letting an unrelated word sharing the same ASCII tail falsely
    // "contain" it (false pass: "über party" read as present inside "schmüber party", since both split
    // down to the shared ASCII remainder "ber"). A trailing or leading apostrophe with nothing on the
    // other side ("steamin'") is simply not part of any token — the apostrophe itself is dropped, never
    // a word on its own, exactly like every other punctuation mark CheckContainment's own remarks
    // describe as a separator.
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:'[\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordTokenRx();

    // MEDIUM-2 review finding — see FindTitleSpans's own remarks for why its needle is collapsed
    // through this before the search.
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRx();
}
