namespace GenWave.Context.Weather;

using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// The F108 reference <see cref="IContextProvider"/>: current conditions + today's high/low from
/// Open-Meteo (keyless, no API key ever touches this class). Constructed already knowing everything
/// it needs (SPEC F107.1's "no request object, no configuration parameter" — the T221 review's
/// SSRF-safe framing): <see cref="OpenMeteoBaseAddress"/> is a fixed host baked into the DI
/// registration, never a caller- or config-supplied URL, and <see cref="IStationLocationProvider"/>
/// is the one seam this class reads coordinates through.
///
/// <para>
/// <b>Fail-closed on coordinates (F108.1).</b> <see cref="IStationLocationProvider.Current"/> is read
/// FRESH on every <see cref="FetchAsync"/> call (never cached on this instance) and validated before
/// any HTTP call is made: a blank or non-numeric/out-of-range latitude or longitude makes this
/// provider behave as if disabled — zero outbound requests, one <see cref="LogLevel.Information"/>
/// line naming the cause (never the coordinate values themselves, satisfying F108.3 even in the
/// failure path). SPEC F108.1 says this is "logged once at Information", not once per fetch attempt
/// (F2 fix, T227 review): this class also implements <see cref="ISelfGatingContextProvider"/>
/// (<see cref="ISelfGatingContextProvider.IsAvailable"/> re-runs the same coordinate check, cheaply
/// and synchronously), which is what <see cref="ContextPipeline"/> actually calls in production — it
/// never reaches <see cref="FetchAsync"/> at all while unavailable, logging its OWN edge-triggered
/// line instead (see that interface's remarks). <see cref="FetchAsync"/>'s own check-and-log below
/// stays as the independent, defense-in-depth backstop for any caller that reaches it directly
/// (bypassing the pipeline) — never the production log source for this cause, but still correct if
/// this property and the live config it reads race apart between the two calls.
/// </para>
///
/// <para>
/// <b>Every other failure returns null, silently.</b> An HTTP error, a timeout, or a malformed/
/// incomplete response body all collapse to <see langword="null"/> — F108.4's "outage triggers
/// F107.6 skip semantics" — with no logging call of any kind from this class: the pipeline already
/// logs one Information line per slot for a null return, and duplicating that here would be a
/// second, redundant line for the exact same cause. This mirrors <c>MusicBrainzYearLookup</c>'s own
/// restraint (no logger dependency for that path at all).
/// </para>
///
/// <para>
/// <b>Spoken-vs-precise split (F108.3).</b> <see cref="StationLocation.SpokenName"/> is the ONLY
/// location string that ever reaches <see cref="ContextContent.SegmentFacts"/>/
/// <see cref="ContextContent.PatterFact"/> — latitude/longitude are used solely to build the
/// outbound Open-Meteo request URL and never appear in any produced string. A blank
/// <see cref="StationLocation.SpokenName"/> means the facts carry conditions with no place name at
/// all (no fallback to the coordinates, no generic "the station" filler).
/// </para>
///
/// <para>
/// <b>Facts are single-line, plain text.</b> <see cref="ContextContent.SegmentFacts"/> and
/// <see cref="ContextContent.PatterFact"/> never contain a newline — a multi-line fact is a
/// prompt-forging hazard (the carry-forward this class's own SPEC section calls out) — every field
/// this class joins into a fact is either already newline-free (a place name, a WMO condition
/// phrase, a formatted number) or is rejected upstream by <see cref="TryParseCoordinates"/> before
/// it ever reaches text. This is true BY CONSTRUCTION, not by trusting Open-Meteo's reply (F1 fix,
/// T227 review): the unit strings that follow every number (<c>°C</c>, <c>km/h</c>) are this class's
/// OWN <see cref="TemperatureUnit"/>/<see cref="WindUnit"/> literals — the exact units this class's
/// own <see cref="BuildRequestUri"/> already pins on the request
/// (<c>temperature_unit=celsius&amp;wind_speed_unit=kmh</c>) — never the reply body's own
/// <c>current_units</c> section, which is unvalidated third-party text a crafted response could fill
/// with a newline or a colon to forge this invariant. <see cref="OpenMeteoResponse"/> consequently
/// has no <c>CurrentUnits</c> property to deserialize that section into at all.
/// </para>
/// </summary>
public sealed class WeatherContextProvider(
    HttpClient http, IStationLocationProvider locationProvider, TimeProvider timeProvider,
    ILogger<WeatherContextProvider> logger) : IContextProvider, ISelfGatingContextProvider
{
    /// <summary>
    /// The fixed, keyless Open-Meteo host (SPEC F108.1) — set as this typed client's
    /// <see cref="HttpClient.BaseAddress"/> in <c>ContextServiceCollectionExtensions</c>, never
    /// overridable by config or a caller. No API key: Open-Meteo's free tier needs none.
    /// </summary>
    public const string OpenMeteoBaseAddress = "https://api.open-meteo.com/";

    /// <summary>
    /// Response-buffer ceiling for this typed client (mirrors <c>MusicBrainzYearLookup.MaxResponseContentBytes</c>'s
    /// own rationale, scaled down: a one-day/current-conditions forecast reply is a few hundred
    /// bytes to a couple KB, never megabytes). Applied via <c>HttpClient.MaxResponseContentBufferSize</c>
    /// in <c>ContextServiceCollectionExtensions</c>.
    /// </summary>
    public const long MaxResponseContentBytes = 65_536;

    /// <summary>How long a successful fetch's content stays servable (F108.4). Deliberately set well
    /// ABOVE the SPEC F108.2 cadence floor (30 minutes) AND its 60-minute default (F3 fix, T227
    /// review): the original 30-minute value sat BELOW the 60-minute default cadence, so on a
    /// healthy, correctly configured station the content would go stale mid-slot every single hour —
    /// a false "stale" skip logged hourly, and the patter lane starved for the back half of every
    /// slot, even though nothing was actually wrong. Two hours comfortably covers every "sane" cadence
    /// an operator is likely to configure (the SPEC floor is a hard MINIMUM of 30, not a ceiling) —
    /// current conditions do not meaningfully change within two hours for a radio patter line, so
    /// there is no accuracy cost to outliving the cadence this generously.</summary>
    static readonly TimeSpan Freshness = TimeSpan.FromHours(2);

    /// <summary>Wind is called out in the facts only once it is this fast (km/h — the request always
    /// asks for <c>wind_speed_unit=kmh</c>, so this threshold means the same thing regardless of any
    /// later query change): below it, a calm/breezy day says nothing about wind at all rather than
    /// padding every fact with an unremarkable number.</summary>
    const double NotableWindKmh = 20.0;

    /// <summary>The unit symbol every rendered temperature carries — fixed, not read from the reply
    /// (F1 fix, see this class's own remarks): matches <see cref="BuildRequestUri"/>'s own
    /// <c>temperature_unit=celsius</c> literal exactly, by construction.</summary>
    const string TemperatureUnit = "°C";

    /// <summary>The unit symbol a called-out wind speed carries — fixed, not read from the reply (F1
    /// fix): matches <see cref="BuildRequestUri"/>'s own <c>wind_speed_unit=kmh</c> literal exactly,
    /// by construction.</summary>
    const string WindUnit = "km/h";

    public string Key => "weather";

    /// <summary>
    /// The <see cref="ISelfGatingContextProvider"/> hook <see cref="ContextPipeline"/> actually calls
    /// in production (F2 fix) — re-runs the same coordinate check <see cref="FetchAsync"/> does below,
    /// cheaply and synchronously, with no HTTP involved. Explicit interface implementation: this is a
    /// pipeline-facing seam, not part of this class's own public surface.
    /// </summary>
    bool ISelfGatingContextProvider.IsAvailable => TryParseCoordinates(locationProvider.Current, out _, out _);

    public async Task<ContextContent?> FetchAsync(CancellationToken ct)
    {
        // Read fresh — never cache IStationLocationProvider.Current on this instance (its own
        // contract) — so a live operator edit to Station:Location:* takes effect on the very next
        // fetch with no restart.
        var location = locationProvider.Current;

        if (!TryParseCoordinates(location, out var latitude, out var longitude))
        {
            // The fail-closed config line (F108.1): names the cause, never the (invalid/blank)
            // values themselves — those could be anything an operator typed, and F108.3 forbids
            // coordinates in a log line even when they are garbage. In production ContextPipeline
            // checks ISelfGatingContextProvider.IsAvailable BEFORE ever reaching this line, so this
            // call site is the defense-in-depth backstop (see this class's own remarks), not the
            // production log source — but it stays independently correct either way.
            logger.LogInformation(
                "Context provider {ProviderKey} is off: Station:Location:Latitude/Longitude is blank or invalid.",
                Key);
            return null;
        }

        try
        {
            var requestUri = BuildRequestUri(latitude, longitude);
            var response = await http.GetAsync(requestUri, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode(); // throws HttpRequestException on non-2xx

            var payload = await response.Content.ReadFromJsonAsync<OpenMeteoResponse>(ct).ConfigureAwait(false);
            if (payload?.Current?.Temperature2m is not { } temperature
                || payload.Current.WeatherCode is not { } weatherCode)
            {
                return null; // Missing required current-conditions fields — an unusable reply.
            }

            return BuildContent(payload, temperature, weatherCode, location.SpokenName, timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Caller cancellation (e.g. shutdown) — not an Open-Meteo fault.
        }
        catch (Exception)
        {
            // Any HTTP/parse failure (timeout, connect failure, non-2xx, malformed JSON) is F108.4's
            // "outage" — the legal skip-never-silence outcome. Silent on the way there; the pipeline
            // logs its own one-Information-line-per-slot for the null this returns.
            return null;
        }
    }

    /// <summary>
    /// Blank means "no coordinate configured" (a legal, common state — weather is off by default);
    /// non-numeric or out-of-range (|latitude| &gt; 90, |longitude| &gt; 180) means "configured wrong".
    /// F108.1 treats both the same way: fail closed, never guess. Parsed with
    /// <see cref="CultureInfo.InvariantCulture"/> — the config value is expected in period-decimal
    /// form regardless of the host's OS/thread culture.
    /// </summary>
    static bool TryParseCoordinates(StationLocation location, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;

        if (string.IsNullOrWhiteSpace(location.Latitude) || string.IsNullOrWhiteSpace(location.Longitude))
            return false;

        if (!double.TryParse(location.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude))
            return false;

        if (!double.TryParse(location.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
            return false;

        return latitude is >= -90.0 and <= 90.0 && longitude is >= -180.0 and <= 180.0;
    }

    /// <summary>
    /// Builds the relative request URI against <see cref="OpenMeteoBaseAddress"/>. Verified at T227
    /// build time against a real reply (<c>curl api.open-meteo.com/v1/forecast?...</c>) shaped like:
    /// <code>
    /// {"current_units":{"temperature_2m":"°C","weather_code":"wmo code","wind_speed_10m":"km/h"},
    ///  "current":{"temperature_2m":22.9,"weather_code":3,"wind_speed_10m":16.9},
    ///  "daily":{"temperature_2m_max":[25.1],"temperature_2m_min":[12.1]}}
    /// </code>
    /// <c>current_units</c> is present in the real reply but deliberately UNUSED (F1 fix, see this
    /// class's own remarks) — <see cref="TemperatureUnit"/>/<see cref="WindUnit"/> are this class's
    /// own fixed literals, matching the <c>temperature_unit</c>/<c>wind_speed_unit</c> query params
    /// below exactly, never that section's own (untrusted) text.
    /// <c>timezone=auto</c> resolves the daily boundary to the coordinate's own local day, so
    /// "today's high/low" means the day the station is actually broadcasting into, regardless of
    /// where the host process itself runs. Coordinates are formatted with
    /// <see cref="CultureInfo.InvariantCulture"/> (never the ambient thread culture) — a comma-decimal
    /// locale would otherwise corrupt the query string (e.g. "51,05" reads as two params).
    /// </summary>
    static string BuildRequestUri(double latitude, double longitude)
    {
        var lat = latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("0.####", CultureInfo.InvariantCulture);

        return "v1/forecast" +
            $"?latitude={lat}&longitude={lon}" +
            "&current=temperature_2m,weather_code,wind_speed_10m" +
            "&daily=temperature_2m_max,temperature_2m_min" +
            "&temperature_unit=celsius&wind_speed_unit=kmh&timezone=auto&forecast_days=1";
    }

    /// <summary>
    /// Renders the fetched payload into single-line, coordinate-free facts (F108.3). The place-name
    /// prefix ("SpokenName: ") is the ONLY spot a colon appears in either produced string — a blank
    /// <paramref name="spokenName"/> therefore produces a fact with no colon at all, the concrete
    /// signal that no place name is being spoken. Unit symbols are <see cref="TemperatureUnit"/>/
    /// <see cref="WindUnit"/> — this class's own literals, never anything read out of
    /// <paramref name="payload"/> (F1 fix, see this class's own remarks).
    /// </summary>
    static ContextContent BuildContent(
        OpenMeteoResponse payload, double temperature, int weatherCode, string spokenName, DateTimeOffset now)
    {
        var condition = DescribeCondition(weatherCode);
        var roundedTemp = FormatWhole(temperature);
        var namePrefix = string.IsNullOrWhiteSpace(spokenName) ? string.Empty : $"{spokenName.Trim()}: ";

        var windClause = BuildWindClause(payload.Current?.WindSpeed10m);
        var forecastClause = BuildForecastClause(payload.Daily);

        var segmentFacts = $"{namePrefix}{condition}, {roundedTemp}{TemperatureUnit}{windClause}.{forecastClause}";
        var patterFact = $"{namePrefix}{condition}, {roundedTemp}{TemperatureUnit}.";

        return new ContextContent(segmentFacts, patterFact, now + Freshness);
    }

    static string BuildWindClause(double? windSpeedKmh)
    {
        if (windSpeedKmh is not { } speed || speed < NotableWindKmh)
            return string.Empty;

        return $", wind {FormatWhole(speed)} {WindUnit}";
    }

    static string BuildForecastClause(OpenMeteoDaily? daily)
    {
        if (daily?.Temperature2mMax is not { Count: > 0 } highs || daily.Temperature2mMin is not { Count: > 0 } lows)
            return string.Empty;

        return $" Today's high {FormatWhole(highs[0])}{TemperatureUnit}, low {FormatWhole(lows[0])}{TemperatureUnit}.";
    }

    /// <summary>Rounds to the nearest whole number and renders it culture-invariantly — cast through
    /// <see langword="int"/> rather than formatting the rounded <see langword="double"/> directly (F5
    /// fix, T227 review): <c>Math.Round(-0.4, AwayFromZero)</c> is IEEE-754 negative zero, and
    /// <c>(-0.0).ToString("0")</c> renders <c>"-0"</c> — a visibly wrong sign on a value that is
    /// actually zero. <see langword="int"/> has no negative-zero representation, so the cast collapses
    /// it to plain <c>0</c> while leaving every genuine negative value's sign untouched.</summary>
    static string FormatWhole(double value) =>
        ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// WMO weather-interpretation code → short spoken phrase, per Open-Meteo's own documented table
    /// (https://open-meteo.com/en/docs — "WMO Weather interpretation codes"). An unrecognized code
    /// (a future Open-Meteo addition this table predates) degrades to a generic phrase rather than a
    /// thrown exception — SPEC F108.4 wants an outage to skip, not a data-shape surprise.
    /// </summary>
    static string DescribeCondition(int weatherCode) => weatherCode switch
    {
        0 => "clear sky",
        1 => "mainly clear",
        2 => "partly cloudy",
        3 => "overcast",
        45 or 48 => "foggy",
        51 or 53 or 55 => "drizzle",
        56 or 57 => "freezing drizzle",
        61 or 63 or 65 => "rain",
        66 or 67 => "freezing rain",
        71 or 73 or 75 or 77 => "snow",
        80 or 81 or 82 => "rain showers",
        85 or 86 => "snow showers",
        95 => "thunderstorms",
        96 or 99 => "thunderstorms with hail",
        _ => "changing conditions",
    };
}
