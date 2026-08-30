// Fixture type for T357's L2-narrowing self-exercising probe (Story290_DependencyLaws.cs,
// ScenarioL2PostgresConfinement). Never wired into any DI container or call path — namespaced
// GenWave.MediaLibrary.Garden (the REAL production namespace text PostgresConfinement.RepositoryLayer
// matches against), the exact shape the T355 review LOW-3 finding warned about: a non-Repository type
// (e.g. a future GardenerService) landing in Garden/ and touching Dapper unnoticed.

using System.Data;
using Dapper;

namespace GenWave.MediaLibrary.Garden;

/// <summary>Garden-namespaced but NOT Repository-named — the one type the narrowed
/// PostgresConfinement.RepositoryLayer must still catch (its own namespace-only predecessor would have
/// wrongly let this through). Extends <see cref="SqlMapper.TypeHandler{T}"/> — the SAME real Dapper
/// dependency shape as production's own DateOnlyTypeHandler/AnnouncementStateTypeHandler, so this
/// probe's Dapper touch is unmistakably genuine, not a synthetic call ArchUnitNET's method-body scan
/// might resolve differently.</summary>
public sealed class NonRepositoryTouchesDapper : SqlMapper.TypeHandler<int>
{
    public override void SetValue(IDbDataParameter parameter, int value) => parameter.Value = value;

    public override int Parse(object value) => (int)value;
}
