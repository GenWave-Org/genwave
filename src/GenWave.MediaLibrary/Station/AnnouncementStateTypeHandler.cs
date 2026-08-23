using System.Data;
using Dapper;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Maps <see cref="AnnouncementState"/> to/from <c>station.announcement.state</c>'s own lowercase text
/// values (SPEC F143.2) — the same "Dapper doesn't natively bridge this scalar shape, register an
/// explicit handler" idiom <see cref="DateOnlyTypeHandler"/> already established for this schema, and
/// the same "do the enum parse explicitly, never trust Dapper's own implicit string-to-enum conversion"
/// discipline <c>PersonaAvatarRepository.ToSource</c>/<c>ToSourceText</c>'s own remarks document. Routed
/// through Dapper's <see cref="SqlMapper.ITypeHandler"/> seam (rather than PersonaAvatarRow's own
/// "keep the row raw, convert at the repository boundary" shape) because <see cref="AnnouncementRow"/>
/// is a positional record Dapper constructs directly from a <c>QueryAsync&lt;AnnouncementRow&gt;</c>
/// call, with no intermediate DTO for a hand-rolled conversion to run against.
///
/// <para>
/// Registered twice, mirroring <see cref="DateOnlyTypeHandler"/>'s own dual registration: production
/// registers it in <see cref="AnnouncementServiceCollectionExtensions.AddAnnouncementStore"/> — this
/// type's only consumer, unlike the shared <c>AddMediaLibrary</c> home <see cref="DateOnlyTypeHandler"/>
/// earned by having multiple repositories reach for it — and tests register it in
/// <c>DatabaseFixture.InitializeAsync</c>, since <c>Harness.AnnouncementRepo</c> constructs
/// <see cref="AnnouncementRepository"/> directly, never through that DI extension.
/// </para>
/// </summary>
sealed class AnnouncementStateTypeHandler : SqlMapper.TypeHandler<AnnouncementState>
{
    public static readonly AnnouncementStateTypeHandler Instance = new();

    public override void SetValue(IDbDataParameter parameter, AnnouncementState value) => parameter.Value = ToText(value);

    public override AnnouncementState Parse(object value) => value switch
    {
        string text => FromText(text),
        _ => throw new NotSupportedException($"Cannot convert {value.GetType()} to {nameof(AnnouncementState)}."),
    };

    static string ToText(AnnouncementState state) => state switch
    {
        AnnouncementState.Pending => "pending",
        AnnouncementState.Claimed => "claimed",
        AnnouncementState.Aired => "aired",
        AnnouncementState.Expired => "expired",
        AnnouncementState.Declined => "declined",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unmapped AnnouncementState."),
    };

    static AnnouncementState FromText(string text) => text switch
    {
        "pending" => AnnouncementState.Pending,
        "claimed" => AnnouncementState.Claimed,
        "aired" => AnnouncementState.Aired,
        "expired" => AnnouncementState.Expired,
        "declined" => AnnouncementState.Declined,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Unmapped station.announcement.state value."),
    };
}
