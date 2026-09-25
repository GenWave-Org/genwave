namespace GenWave.Host.Configuration;

/// <summary>
/// The UI input kind for an operator-editable setting.
/// Drives the admin UI to render a checkbox (Boolean), a numeric input (Number), or a text
/// input (String).
/// </summary>
public enum SettingKind
{
    /// <summary>A <c>true</c>/<c>false</c> toggle — rendered as a checkbox.</summary>
    Boolean,

    /// <summary>A numeric value — rendered as <c>&lt;input type="number"&gt;</c>.</summary>
    Number,

    /// <summary>
    /// A list of integer values — rendered as a multi-value input (e.g. a library-id picker).
    /// Values are stored and exchanged as colon-indexed IConfiguration keys
    /// (<c>:0</c>, <c>:1</c>, …).
    /// </summary>
    NumberList,

    /// <summary>
    /// A free-text string value (e.g. an endpoint URL or an LLM model name) — rendered as
    /// <c>&lt;input type="text"&gt;</c> (SPEC F36.2, STORY-124).
    /// </summary>
    String,

    /// <summary>
    /// A closed set of allowed string values (e.g. a theme slug) — rendered as a
    /// <c>&lt;select&gt;</c>, never a free-text input. <see cref="AllowedSetting.Choices"/> carries
    /// the valid set; unlike <see cref="String"/>, a value outside it is a validation error, not a
    /// silently-unresolvable typo (SPEC F102.14, STORY-265).
    /// </summary>
    Choice,

    /// <summary>
    /// A closed set of allowed string values, zero or more of which may be selected at once (e.g.
    /// show slugs a feature is scoped to) — rendered as a checkbox per value, never a free-text
    /// input (SPEC F205.7, STORY-482, PLAN T585). The stored value is unchanged from before this
    /// kind existed: a JSON array of the selected values (<c>Crosstalk:Shows</c>'s own pre-existing
    /// slug-array shape) — <see cref="SettingValidator"/> keeps its per-key shape check rather than
    /// gaining a generic one. Like <see cref="Choice"/>, a value outside the resolved set is never
    /// itself rejected (SPEC F205.7a) — see <see cref="ISettingChoiceResolver"/>'s own remarks for
    /// why.
    /// </summary>
    MultiChoice,
}
