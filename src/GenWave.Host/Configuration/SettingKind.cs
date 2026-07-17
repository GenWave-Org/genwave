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
}
