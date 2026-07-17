// STORY-047 — Library CRUD endpoints (WIRE)
//
// BDD specification — xUnit.
//
// Runnable scenarios (no live stack): construct LibrariesController directly with fakes.
// Operator-gated scenarios (require live stack + cookie auth): marked Integration + Skip.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Auth;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory <see cref="ILibraryRepository"/> that returns a fixed list of libraries.
/// </summary>
file sealed class FakeLibraryRepository(IReadOnlyList<LibraryAdminInfo> libraries) : ILibraryRepository
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            libraries.Where(l => ids.Contains(l.Id))
                     .Select(l => new LibraryInfo(l.Id, l.Name))
                     .ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult(libraries);
}

/// <summary>
/// Scriptable <see cref="IAdminLibraryWrite"/>: configure the result for each operation before
/// calling the controller. Defaults to throwing if not configured.
/// </summary>
file sealed class FakeAdminLibraryWrite : IAdminLibraryWrite
{
    public LibraryWriteResult? CreateResult  { get; set; }
    public LibraryWriteResult? RenameResult  { get; set; }
    public LibraryWriteResult? DeleteResult  { get; set; }

    public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct)
        => Task.FromResult(CreateResult ?? throw new InvalidOperationException("CreateResult not set"));

    public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct)
        => Task.FromResult(RenameResult ?? throw new InvalidOperationException("RenameResult not set"));

    public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct)
        => Task.FromResult(DeleteResult ?? throw new InvalidOperationException("DeleteResult not set"));
}

/// <summary>
/// Builds a <see cref="LibrariesController"/> with the given repo + write fakes.
/// </summary>
file static class LibrariesControllerFactory
{
    public static LibrariesController Build(
        ILibraryRepository repo,
        IAdminLibraryWrite write) =>
        new(repo, write, NullLogger<LibrariesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureLibraryCrudEndpointsInProcess
{
    // GET /api/libraries ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioListLibraries
    {
        [Fact]
        public async Task GetReturnsAllLibrariesWithIdNameAndMediaCount()
        {
            var libraries = new List<LibraryAdminInfo>
            {
                new(1, "Main", 42),
                new(2, "Deep cuts", 7),
            };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository(libraries),
                new FakeAdminLibraryWrite());

            var result = await controller.List(CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var dtos = Assert.IsAssignableFrom<LibraryDto[]>(ok.Value);
            Assert.Equal(2, dtos.Length);
            Assert.Equal(new LibraryDto(1, "Main", 42),       dtos[0]);
            Assert.Equal(new LibraryDto(2, "Deep cuts", 7),   dtos[1]);
        }

        [Fact]
        public async Task GetReturnsEmptyArrayWhenNoLibrariesExist()
        {
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                new FakeAdminLibraryWrite());

            var result = await controller.List(CancellationToken.None);

            var ok   = Assert.IsType<OkObjectResult>(result);
            var dtos = Assert.IsAssignableFrom<LibraryDto[]>(ok.Value);
            Assert.Empty(dtos);
        }
    }

    // POST /api/libraries ────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioCreateLibrary
    {
        [Fact]
        public async Task PostWithValidNameReturns201WithIdAndName()
        {
            var write = new FakeAdminLibraryWrite { CreateResult = new LibraryWriteResult.Created(5) };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Create(new LibraryNameRequest("jazz"), CancellationToken.None);

            var created = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
            var dto = Assert.IsType<LibraryDto>(created.Value);
            Assert.Equal(5,      dto.Id);
            Assert.Equal("jazz", dto.Name);
            Assert.Equal(0,      dto.MediaCount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task PostWithBlankNameReturns400(string? name)
        {
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                new FakeAdminLibraryWrite());

            var result = await controller.Create(new LibraryNameRequest(name), CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task PostWithDuplicateNameReturns409()
        {
            var write = new FakeAdminLibraryWrite { CreateResult = new LibraryWriteResult.NameConflict() };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Create(new LibraryNameRequest("Main"), CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.IsType<ProblemDetails>(conflict.Value);
        }
    }

    // PATCH /api/libraries/{id} ──────────────────────────────────────────────────────────────────

    public sealed class ScenarioRenameLibrary
    {
        [Fact]
        public async Task PatchWithValidNameReturns200()
        {
            var write = new FakeAdminLibraryWrite { RenameResult = new LibraryWriteResult.Renamed() };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Rename(1, new LibraryNameRequest("new name"), CancellationToken.None);

            Assert.IsType<OkResult>(result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task PatchWithBlankNameReturns400(string? name)
        {
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                new FakeAdminLibraryWrite());

            var result = await controller.Rename(1, new LibraryNameRequest(name), CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task PatchWithUnknownIdReturns404()
        {
            var write = new FakeAdminLibraryWrite { RenameResult = new LibraryWriteResult.NotFound() };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Rename(999, new LibraryNameRequest("x"), CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task PatchWithDuplicateNameReturns409()
        {
            var write = new FakeAdminLibraryWrite { RenameResult = new LibraryWriteResult.NameConflict() };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Rename(2, new LibraryNameRequest("Main"), CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.IsType<ProblemDetails>(conflict.Value);
        }
    }

    // DELETE /api/libraries/{id} ─────────────────────────────────────────────────────────────────

    public sealed class ScenarioDeleteLibrary
    {
        [Fact]
        public async Task DeleteEmptyLibraryReturns204()
        {
            var write = new FakeAdminLibraryWrite { DeleteResult = new LibraryWriteResult.Deleted() };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Delete(1, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task DeleteUnknownIdReturns404()
        {
            var write = new FakeAdminLibraryWrite { DeleteResult = new LibraryWriteResult.NotFound() };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Delete(999, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteNonEmptyLibraryReturns409WithDependentMediaCount()
        {
            var write = new FakeAdminLibraryWrite
            {
                DeleteResult = new LibraryWriteResult.HasDependents(17),
            };
            var controller = LibrariesControllerFactory.Build(
                new FakeLibraryRepository([]),
                write);

            var result = await controller.Delete(1, CancellationToken.None);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var problem  = Assert.IsType<ProblemDetails>(conflict.Value);
            Assert.True(problem.Extensions.ContainsKey("dependentMediaCount"));
            Assert.Equal(17, (int)(problem.Extensions["dependentMediaCount"] ?? 0));
        }
    }
}

// ── Operator-gated (live stack) ───────────────────────────────────────────────────────────────────

public static class FeatureLibraryCrudEndpoints
{
    const string Pending = "Pending L2 — library CRUD endpoints; operator-gated live, see docs/PLAN.md Epic J";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioListingLibrariesShowsEveryLibraryWithItsMediaCount
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void GetApiLibrariesReturnsEveryLibraryWithIdNameAndMediaCount()
        {
            // GET /api/libraries returns every row in library.library + a per-row mediaCount.
            // The list is NOT filtered by Station:Scope:LibraryIds — library management operates above scope.
            Assert.Fail("pending L2");
        }
    }

    public sealed class ScenarioCreatingALibrary
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PostWithAValidNameReturns201AndTheNewId()
        {
            // POST /api/libraries { "name": "deep cuts" } → 201 with body { id: <new>, name: "deep cuts" }.
            Assert.Fail("pending L2");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void NewlyCreatedLibraryIsNotAutoAddedToStationScope()
        {
            // After POST /api/libraries returns id=2, Station:Scope:LibraryIds remains [1]. A reassign into
            // library 2 will leave the rotation until the operator widens scope via F19.
            Assert.Fail("pending L2");
        }
    }

    public sealed class ScenarioRenamingALibrary
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PatchWithANewNameReturns200AndPersistsTheNewName()
        {
            // PATCH /api/libraries/{id} { "name": "main rotation" } → 200; the row's name is updated.
            Assert.Fail("pending L2");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void SeedLibraryAtId1FollowsTheSameRenameRule()
        {
            // PATCH /api/libraries/1 { "name": "main rotation" } succeeds — id is the stable identifier, not name.
            Assert.Fail("pending L2");
        }
    }

    public sealed class ScenarioDeletingAnEmptyLibrary
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void DeleteOnAnEmptyLibraryReturns204AndRemovesTheRow()
        {
            // For a library whose mediaCount = 0: DELETE /api/libraries/{id} → 204; the row is gone from library.library.
            Assert.Fail("pending L2");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDuplicateName
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PostWithADuplicateNameReturns409AndWritesNothing()
        {
            // POST /api/libraries { "name": "existing-name" } → 409 ProblemDetails; library.library is unchanged.
            Assert.Fail("pending L2");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void PatchToADuplicateNameReturns409AndWritesNothing()
        {
            // PATCH /api/libraries/{otherId} { "name": "existing-name" } → 409; the row's name is unchanged.
            Assert.Fail("pending L2");
        }
    }

    public sealed class ScenarioDeletingANonEmptyLibrary
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void DeleteOnANonEmptyLibraryReturns409WithDependentMediaCount()
        {
            // DELETE /api/libraries/{id} on a library with N media rows → 409 ProblemDetails with body
            // { dependentMediaCount: N }. The row remains in library.library.
            Assert.Fail("pending L2");
        }
    }

    public sealed class ScenarioValidationAndAuth
    {
        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void BlankNameOnPostOrPatchReturns400()
        {
            // name="" or whitespace-only → 400 ProblemDetails; nothing is written.
            Assert.Fail("pending L2");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void UnknownIdOnPatchOrDeleteReturns404()
        {
            // PATCH/DELETE /api/libraries/{nonexistent-id} → 404 ProblemDetails.
            Assert.Fail("pending L2");
        }

        [Fact(Skip = Pending), Trait("Category", "Integration")]
        public void WriteWithoutCookieOrWithNonJsonContentTypeIsRejected()
        {
            // With Admin:Password set: no cookie → 401; POST/PATCH non-JSON Content-Type → 415.
            Assert.Fail("pending L2");
        }
    }
}
