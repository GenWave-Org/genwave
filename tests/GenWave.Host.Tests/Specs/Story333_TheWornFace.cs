// STORY-333 — The worn face (SPEC F128.5/.6/.9 · PLAN T291 pipeline + T295 endpoints)
//
// BDD specification — xUnit. The normalize pipeline's own gates pin to T291; the
// endpoints pin to T295. The Personas-page render/placeholder/offer UI (AC2/AC4's UI
// halves) lives in admin-ui jest (persona-faces.spec.tsx) + the T301 wire.
//
// T291's five facts below run against the REAL ImageNormalizeService/FfmpegImageProcessRunner —
// real ffmpeg, real generated PNG/JPEG fixtures (TestImages, mirrors GenWave.MediaLibrary.Tests'
// own TestMedia idiom) — never a mock of the pipeline itself. The two "before ffmpeg" gate facts
// substitute CountingImageProcessRunner in IImageProcessRunner's place instead: proof that a
// rejected input never reached ffmpeg at all, not merely that the right failure reason came back.
//
// WIRED T295 — every write-path Fact below drives the real production route through
// WebApplicationFactory<Program> (real routing/auth/content-negotiation pipeline, real ffmpeg via
// the real ImageNormalizeService — no mock of the re-validation pipeline itself, mirrors this
// file's own T291 posture and Story332_AvatarPacksIntoTheLibrary.cs's own WIRED T293 posture)
// against FakePersonaStore/FakePersonaAvatarStore/FakeAvatarPackStore (this project has no
// Postgres fixture; the REAL station.persona_avatar SQL — including the true replace-whole-row
// upsert — is T290's own coverage against real Postgres, GenWave.MediaLibrary.Tests).

namespace GenWave.Host.Tests.Specs;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Images;
using GenWave.Host.Tests.Fakes;

public static class FeatureTheWornFace
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the pipeline (T291)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheNormalizePipelineProducesACleanFace
    {
        static ImageNormalizeService BuildRealService() =>
            new(new FfmpegImageProcessRunner(), NullLogger<ImageNormalizeService>.Instance);

        [Fact]
        public async Task OutputIsAFresh512SquarePng()
        {
            // Given a real, plausibly-sized, non-square JPEG (any accepted input)...
            var input = TestImages.CreateJpeg(400, 300);

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then a fresh 512×512 PNG results — probed independently via ffprobe, not by
            // trusting the service's own OutputDimensionPx constant back at itself.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            var (width, height, codec) = await ProbePngAsync(success.Bytes);
            Assert.Equal("png", codec);
            Assert.Equal(512, width);
            Assert.Equal(512, height);
        }

        [Fact]
        public async Task NonSquareInputIsCenterCropped()
        {
            // Given a 900×300 PNG: three solid 300×300 bands side by side, red | green | blue.
            // min(iw,ih) = 300, so a CORRECT center crop takes x ∈ [300,600) — the middle band
            // ONLY — and discards the outer red/blue bands entirely before ever scaling. This
            // fixture kills the scale-then-crop mutant (scale the whole 900×300 frame down to
            // 512×512 FIRST, which squashes rather than discards the outer bands, then a
            // now-square crop is a no-op): under the mutant, the squashed red/blue bands are still
            // visible at the output's own left/right edges, whereas the correct crop-then-scale
            // pipeline is uniformly green edge to edge, no red or blue anywhere.
            var input = TestImages.CreateThreeBandPng("red", "green", "blue", bandWidth: 300);

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then the ENTIRE output reads green — center same as both edges — proof the crop
            // discarded the outer bands before scaling, not merely that a mid-frame sample happens
            // to land on green (which a squash alone would already satisfy).
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            var centerPixel = await SamplePixelAsync(success.Bytes, x: 256, y: 256);
            var leftEdgePixel = await SamplePixelAsync(success.Bytes, x: 10, y: 256);
            var rightEdgePixel = await SamplePixelAsync(success.Bytes, x: 502, y: 256);

            Assert.True(centerPixel.G > centerPixel.R && centerPixel.G > centerPixel.B,
                $"expected a green center pixel, got {centerPixel}");
            Assert.True(leftEdgePixel.G > leftEdgePixel.R,
                $"expected the left edge to be green (red band discarded, not squashed-in), got {leftEdgePixel}");
            Assert.True(rightEdgePixel.G > rightEdgePixel.B,
                $"expected the right edge to be green (blue band discarded, not squashed-in), got {rightEdgePixel}");
        }

        [Fact]
        public async Task MetadataIsStructurallyAbsentFromTheOutput()
        {
            // Given a PNG carrying a real, correctly-CRC'd tEXt chunk — self-verified here so a
            // broken fixture (one that never actually carried metadata) can't pass this fact for
            // the wrong reason...
            var plain = TestImages.CreatePng(400, 400);
            var input = TestImages.WithTextChunk(plain, "Comment", "secret gps location data");
            Assert.Contains("tEXt", TestImages.PngChunkTypes(input));

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then the output carries no text/EXIF chunk at all — EXIF/GPS/text chunks in the
            // input do not survive the re-encode.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            var outputChunkTypes = TestImages.PngChunkTypes(success.Bytes);
            Assert.DoesNotContain("tEXt", outputChunkTypes);
            Assert.DoesNotContain("iTXt", outputChunkTypes);
            Assert.DoesNotContain("zTXt", outputChunkTypes);
            Assert.DoesNotContain("eXIf", outputChunkTypes);
        }

        [Fact]
        public async Task HighBitDepthInputLandsUnderTheOutputCeiling()
        {
            // Given a real, high-entropy 16-bit-per-channel (rgba64be) PNG — noise-filled so it
            // doesn't compress away, the exact shape whose un-pinned re-encode measured 1.78 MiB
            // at 512×512 (ImageNormalizeService.MaxOutputBytes's own remarks)...
            var input = TestImages.CreateHighBitDepthPng(600, 600);

            // When it is normalized...
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then the output still succeeds AND lands at or under ImageNormalizeService's own
            // MaxOutputBytes ceiling (768 KiB since gh-#520 — that constant's own remarks carry the
            // full honest history) — proof the -pix_fmt rgba pin (not merely the defensive ceiling
            // check) keeps a high-bit-depth input's output bounded.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            Assert.True(success.Bytes.Length <= ImageNormalizeService.MaxOutputBytes,
                $"expected <= {ImageNormalizeService.MaxOutputBytes} bytes, got {success.Bytes.Length}");
        }

        [Fact]
        public async Task RealHighEntropyArtReencodesUnderTheRaisedCeilingAtMaxCompressionSettings()
        {
            // Given the ACTUAL gh-#520 bug-report upload-path asset — a real, large (1,948,878-byte,
            // 1024×1024) PNG carrying genuine ancillary chunks (Fixtures/README.md's own provenance
            // entry): the measured class that used to refuse under the OLD 512 KiB ceiling/default
            // ffmpeg settings (a 629,193-byte re-encode) but now succeeds under the raised 768 KiB
            // ceiling with BuildFfmpegArgs's own -compression_level 100 -pred mixed (a measured
            // 541,265-byte re-encode),
            var input = TestImages.LoadRealArtLargeUpload();

            // When it is normalized (the real ImageNormalizeService/FfmpegImageProcessRunner pipeline,
            // real max-compression ffmpeg args — no mock of the encoder itself),
            var result = await BuildRealService().NormalizeAsync(input, CancellationToken.None);

            // Then it succeeds, and the re-encoded output lands UNDER the raised 768 KiB ceiling — the
            // gh-#520 fix, proven against the actual bug-report asset rather than a synthetic stand-in.
            var success = Assert.IsType<ImageNormalizeResult.Success>(result);
            Assert.True(success.Bytes.Length < ImageNormalizeService.MaxOutputBytes,
                $"expected < {ImageNormalizeService.MaxOutputBytes} bytes, got {success.Bytes.Length}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the write paths (T295)
    // ---------------------------------------------------------------------

    public sealed class ScenarioWearingAPackFaceCopiesIt
    {
        [Fact]
        public async Task TheFaceRowIsACopyWithCatalogProvenance()
        {
            // Given an installed avatar pack with one item, and a persona with no face yet,
            var avatarPackStore = await PersonaAvatarFixtures.SeededAvatarPackStoreAsync();
            var personaAvatarStore = new FakePersonaAvatarStore();
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(),
                personaAvatarStore: personaAvatarStore, avatarPackStore: avatarPackStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When POST /api/personas/{id}/avatar/from-pack is called naming that pack + item (the
            // real production route),
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar/from-pack",
                new { packSlug = PersonaAvatarFixtures.PackSlug, itemName = PersonaAvatarFixtures.ItemName });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the stored row is a COPY carrying catalog provenance — source='catalog',
            // imported_from=the pack slug — never a live reference back into the pack.
            var stored = await personaAvatarStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None);
            Assert.Equal(
                (PersonaAvatarSource.Catalog, PersonaAvatarFixtures.PackSlug),
                (stored?.Source, stored?.ImportedFrom));
        }

        [Fact]
        public async Task TheTokenRotatesOnTheWrite()
        {
            // Given a persona already wearing an UPLOADED face under its own token,
            var avatarPackStore = await PersonaAvatarFixtures.SeededAvatarPackStoreAsync();
            var personaAvatarStore = new FakePersonaAvatarStore();
            const string priorToken = "prior-upload-token";
            await personaAvatarStore.UpsertAsync(
                new PersonaAvatarInput(
                    PersonaAvatarFixtures.KnownPersonaId, TestImages.CreatePng(512, 512), "prior-sha",
                    priorToken, PersonaAvatarSource.Upload, null),
                CancellationToken.None);
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(),
                personaAvatarStore: personaAvatarStore, avatarPackStore: avatarPackStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When that SAME persona's face is applied from a pack — a write that replaces the row
            // wholesale, including the token,
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar/from-pack",
                new { packSlug = PersonaAvatarFixtures.PackSlug, itemName = PersonaAvatarFixtures.ItemName });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the token is a FRESH value — never the prior upload's own token.
            var stored = await personaAvatarStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None);
            Assert.NotEqual(priorToken, stored?.Token);
        }
    }

    public sealed class ScenarioOwnerUploadWearsAndRemoves
    {
        [Fact]
        public async Task PutStoresTheNormalizedFaceWithSourceUpload()
        {
            // Given a persona with no face yet, and a real, valid, non-square JPEG body (any
            // accepted input — the pipeline's own normalize correctness is T291's coverage above;
            // this Fact's job is proving the WRITE PATH persists the real output with source=upload),
            var personaAvatarStore = new FakePersonaAvatarStore();
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            using var content = PersonaAvatarFixtures.ImageBody(TestImages.CreateJpeg(400, 300), "image/jpeg");

            // When PUT /api/personas/{id}/avatar is called with the raw bytes (the real production
            // route, real ffmpeg),
            var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the stored row carries source=upload.
            var stored = await personaAvatarStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None);
            Assert.Equal(PersonaAvatarSource.Upload, stored?.Source);
        }

        [Fact]
        public async Task DeleteRemovesTheRow()
        {
            // Given a persona wearing a face,
            var personaAvatarStore = new FakePersonaAvatarStore();
            await personaAvatarStore.UpsertAsync(
                new PersonaAvatarInput(
                    PersonaAvatarFixtures.KnownPersonaId, TestImages.CreatePng(512, 512), "sha",
                    "seeded-token", PersonaAvatarSource.Upload, null),
                CancellationToken.None);
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When DELETE /api/personas/{id}/avatar is called,
            var response = await client.DeleteAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar");

            // Then it responds 204 and the row is gone.
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Null(await personaAvatarStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — hostile input dies at the gate (T291/T295)
    // ---------------------------------------------------------------------

    public sealed class ScenarioHostileUploadsDieQuietlyAtTheGates
    {
        [Fact]
        public async Task ANonImageBodyFailsTheMagicGateBeforeAnyDecoderRuns()
        {
            // Given a body that is neither PNG nor JPEG by signature...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var notAnImage = "this is definitely not an image"u8.ToArray();

            // When it is normalized...
            var result = await service.NormalizeAsync(notAnImage, CancellationToken.None);

            // Then it is rejected at the magic-bytes gate, and ffmpeg is NEVER invoked — proof the
            // gate runs BEFORE any decoder touches the bytes, not merely that the right reason
            // came back.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.NotAnImage, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task OversizeDimensionsFailTheHeaderGateBeforeFfmpeg()
        {
            // Given a synthetic PNG header (signature + IHDR only, no pixel data at all) announcing
            // dimensions on either side of the SPEC F128.6 bounds...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var oversized = TestImages.MakeSyntheticPngHeader(width: 5000, height: 5000);
            var undersized = TestImages.MakeSyntheticPngHeader(width: 128, height: 128);

            // When each is normalized...
            var oversizedResult = await service.NormalizeAsync(oversized, CancellationToken.None);
            var undersizedResult = await service.NormalizeAsync(undersized, CancellationToken.None);

            // Then both are rejected at the header-dimensions gate — >4096px (the
            // decompression-bomb class) and <256px alike — and ffmpeg is NEVER invoked for either:
            // the gate reads IHDR alone, no decoder ever sees these bytes.
            var oversizedFailure = Assert.IsType<ImageNormalizeResult.Failure>(oversizedResult);
            Assert.Equal(ImageNormalizeFailureReason.DimensionsTooLarge, oversizedFailure.Reason);
            var undersizedFailure = Assert.IsType<ImageNormalizeResult.Failure>(undersizedResult);
            Assert.Equal(ImageNormalizeFailureReason.DimensionsTooSmall, undersizedFailure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task AnimatedApngFailsTheAnimationGateBeforeFfmpeg()
        {
            // Given a real APNG (ffmpeg's own apng muxer) — self-verified here so a broken
            // fixture (one that never actually carried acTL) can't pass this fact for the wrong
            // reason...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var apng = TestImages.CreateApng(300, 300);
            Assert.Contains("acTL", TestImages.PngChunkTypes(apng));

            // When it is normalized...
            var result = await service.NormalizeAsync(apng, CancellationToken.None);

            // Then it is rejected as Animated, and ffmpeg is NEVER invoked — SPEC F128.1's
            // "acTL rejected" rule holds at the upload gate too, before any decoder runs.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.Animated, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task OversizeBodyFailsTheBoundedReadGateBeforeFfmpeg()
        {
            // Given a body one byte over ImageNormalizeService.MaxInputBytes...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);
            var oversizeBody = new byte[ImageNormalizeService.MaxInputBytes + 1];

            // When it is normalized...
            var result = await service.NormalizeAsync(oversizeBody, CancellationToken.None);

            // Then it is rejected as TooLarge, and ffmpeg is NEVER invoked — the bounded-read gate
            // runs before even the magic-bytes check.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.TooLarge, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task EmptyBodyFailsTheBoundedReadGateBeforeFfmpegWithItsOwnReason()
        {
            // Given an empty body...
            var runner = new CountingImageProcessRunner();
            var service = new ImageNormalizeService(runner, NullLogger<ImageNormalizeService>.Instance);

            // When it is normalized...
            var result = await service.NormalizeAsync([], CancellationToken.None);

            // Then it is rejected as Empty — distinct from TooLarge, so an empty upload never
            // misreads as an oversize one in logs or the T295/T307 ProblemDetails mapping — and
            // ffmpeg is NEVER invoked.
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.Empty, failure.Reason);
            Assert.Equal(0, runner.InvocationCount);
        }

        [Fact]
        public async Task ADecodeFailureLeavesThePreviousFaceUnchanged()
        {
            // Given a persona already wearing a face,
            var personaAvatarStore = new FakePersonaAvatarStore();
            var priorFace = new PersonaAvatarInput(
                PersonaAvatarFixtures.KnownPersonaId, TestImages.CreatePng(512, 512), "prior-sha",
                "prior-token", PersonaAvatarSource.Upload, null);
            await personaAvatarStore.UpsertAsync(priorFace, CancellationToken.None);
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When a REAL, header-valid-but-corrupt PNG (valid signature + in-bounds IHDR, garbage
            // IDAT) is PUT — genuinely reaching ffmpeg and genuinely failing there (EncodeFailed via
            // the REAL binary's own non-zero exit, never a fake failure),
            using var content = PersonaAvatarFixtures.ImageBody(TestImages.CreateCorruptPng(400, 400));
            var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // Then the PREVIOUS face survives untouched — the failing write never reached
            // IPersonaAvatarStore.UpsertAsync at all.
            var stillWorn = await personaAvatarStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None);
            Assert.Equal(priorFace.Sha256, stillWorn?.Sha256);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the ProblemDetails mapping is honest, per reason (T295 rider)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheProblemDetailsMappingIsHonestPerReason
    {
        [Fact]
        public async Task EncodeFailedNeverReadsAsADecodeError()
        {
            // Given a persona, and a real corrupt-but-header-valid PNG that genuinely fails INSIDE
            // ffmpeg (the same live repro ADecodeFailureLeavesThePreviousFaceUnchanged uses),
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            using var content = PersonaAvatarFixtures.ImageBody(TestImages.CreateCorruptPng(400, 400));

            // When it is PUT,
            var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused (400) with an HONEST reason — EncodeFailed covers a genuinely
            // corrupt input, a missing ffmpeg binary, AND the defensive output-byte-ceiling case
            // alike (ImageNormalizeService.MaxOutputBytes's own remarks), none of which is a
            // "decode" problem specifically, so the body must never call this a decode error.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain("decode", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task EveryFailureReasonGetsItsOwnDistinctTitle()
        {
            // Given one request per ImageNormalizeFailureReason reachable through a real PUT — Empty,
            // TooLarge, NotAnImage, DimensionsTooSmall, DimensionsTooLarge, Animated, and EncodeFailed
            // (ImageNormalizeProblemMapper's own switch — EXTRACTED from this controller at PLAN T307's
            // own second-copy moment, StationImageController now shares the identical mapping) — PLUS
            // OutputTooLarge (gh-#520), mapped DIRECTLY rather than through an eighth PUT: reaching it
            // for real needs a specially-rigged oversize-producing IImageProcessRunner (this file's own
            // ASuccessfulReencodeThatIsMerelyTooBigReturnsOutputTooLargeNotEncodeFailed Fact drives that
            // full round trip instead) — this Fact only needs the MAPPER's own output for the eighth
            // title, which is the ONE thing under test here, not a second HTTP wiring,
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            async Task<string?> TitleForAsync(byte[] body)
            {
                using var content = PersonaAvatarFixtures.ImageBody(body);
                var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var problem = JsonSerializer.Deserialize<ProblemDetails>(await response.Content.ReadAsStringAsync());
                return problem?.Title;
            }

            var titles = new[]
            {
                await TitleForAsync([]),                                            // Empty
                await TitleForAsync(new byte[ImageNormalizeService.MaxInputBytes + 1]), // TooLarge
                await TitleForAsync("not an image"u8.ToArray()),                    // NotAnImage
                await TitleForAsync(TestImages.MakeSyntheticPngHeader(128, 128)),    // DimensionsTooSmall
                await TitleForAsync(TestImages.MakeSyntheticPngHeader(5000, 5000)),  // DimensionsTooLarge
                await TitleForAsync(TestImages.CreateApng(300, 300)),                // Animated
                await TitleForAsync(TestImages.CreateCorruptPng(400, 400)),          // EncodeFailed
                ImageNormalizeProblemMapper.ToProblem(ImageNormalizeFailureReason.OutputTooLarge).Title, // OutputTooLarge
            };

            // When mapped to ProblemDetails — then no two reasons share a title: honest, per-reason
            // mapping means every distinct cause reads as a distinct claim, over the enum's own full
            // eight-member set.
            Assert.Equal(titles.Length, titles.Distinct().Count());
        }

        [Fact]
        public async Task ASuccessfulReencodeThatIsMerelyTooBigReturnsOutputTooLargeNotEncodeFailed()
        {
            // Given ffmpeg genuinely "succeeding" — a fake IImageProcessRunner that writes a real
            // PNG-signed but over-ceiling file — the only DETERMINISTIC way to hit the ceiling branch:
            // a real re-encode at max settings never approaches it (this file's own
            // RealHighEntropyArtReencodesUnderTheRaisedCeilingAtMaxCompressionSettings Fact proves
            // that directly),
            var service = new ImageNormalizeService(new OversizeOutputImageProcessRunner(), NullLogger<ImageNormalizeService>.Instance);
            var input = TestImages.CreateJpeg(400, 300);

            // When it is normalized,
            var result = await service.NormalizeAsync(input, CancellationToken.None);

            // Then the failure reason is OutputTooLarge — NEVER EncodeFailed — since ffmpeg genuinely
            // produced a valid, decodable PNG; it was merely too large to store (gh-#520's own honest
            // split, discharging the standing T295 rider).
            var failure = Assert.IsType<ImageNormalizeResult.Failure>(result);
            Assert.Equal(ImageNormalizeFailureReason.OutputTooLarge, failure.Reason);
        }

        [Fact]
        public void OutputTooLargeMapsToAnHonestTooLargeToStoreProblemNeverTheEncodeFailedCopy()
        {
            // Given OutputTooLarge and EncodeFailed — both ffmpeg-stage reasons, but distinct causes
            // (gh-#520: a SUCCESSFUL re-encode that is merely too big, vs ffmpeg genuinely failing),
            var outputTooLarge = ImageNormalizeProblemMapper.ToProblem(ImageNormalizeFailureReason.OutputTooLarge);
            var encodeFailed = ImageNormalizeProblemMapper.ToProblem(ImageNormalizeFailureReason.EncodeFailed);

            // When compared — then OutputTooLarge's own Detail is the HONEST "too large to store" copy
            // (never EncodeFailed's generic "could not be processed" one — gh-#520's own root-cause
            // report: the over-ceiling case used to read as a misleading decode failure), and the two
            // carry distinct titles.
            Assert.Equal(
                (Detail: "The processed image is too large to store.", TitlesDiffer: true),
                (outputTooLarge.Detail, TitlesDiffer: outputTooLarge.Title != encodeFailed.Title));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — token entropy (T290/T295 rider: shape + rotation + uniqueness by construction)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheTokenIsCryptographicallyRandom
    {
        [Fact]
        public async Task EveryWriteMintsA128BitLowercaseHexTokenDistinctFromTheLast()
        {
            // Given a persona with no face yet,
            var personaAvatarStore = new FakePersonaAvatarStore();
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            var bytes = TestImages.CreatePng(512, 512);

            async Task<string> PutAndReadTokenAsync()
            {
                using var content = PersonaAvatarFixtures.ImageBody(bytes);
                var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                var dto = await response.Content.ReadFromJsonAsync<PersonaAvatarDto>();
                return dto?.Token ?? throw new InvalidOperationException("PUT succeeded with no token in its own response.");
            }

            // When the SAME persona is written to twice in a row — two independent uploads of the
            // exact SAME bytes both times, so only the controller's own fresh mint (never a
            // difference in payload) could ever explain a token difference,
            var firstToken = await PutAndReadTokenAsync();
            var secondToken = await PutAndReadTokenAsync();

            // Then both tokens are shaped as 128-bit lowercase hex (the F129.1/F88 opaque-token
            // idiom — uniqueness of any ONE such value is by construction, never a database round
            // trip: PersonaAvatarController's own TOKEN ENTROPY remarks) AND the second write's
            // token is never the first's — the row was genuinely rotated, not merely re-read.
            Assert.True(IsWellFormedToken(firstToken), $"expected 32 lowercase hex chars, got \"{firstToken}\"");
            Assert.True(IsWellFormedToken(secondToken), $"expected 32 lowercase hex chars, got \"{secondToken}\"");
            Assert.NotEqual(firstToken, secondToken);
        }

        // Mirrors ArtworkTokenRepository.IsWellFormed verbatim (SPEC F88.2's own shape check) — the
        // SAME 32-lowercase-hex contract, proven here against a REAL PersonaAvatarController
        // response rather than re-asserted in isolation.
        static bool IsWellFormedToken(string token)
        {
            if (token.Length != 32)
                return false;

            foreach (var c in token)
                if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                    return false;

            return true;
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — object-level 404s (T295: the route id/pack/item must name a real row)
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnknownPersonaIs404
    {
        [Fact]
        public async Task PutToAnUnknownPersonaIs404()
        {
            // Given no persona seeded at all,
            await using var factory = new PersonaAvatarWebFactory(personaStore: new FakePersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            using var content = PersonaAvatarFixtures.ImageBody(TestImages.CreatePng(512, 512));

            // When PUT /api/personas/{id}/avatar names an id no persona holds,
            var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.UnknownPersonaId}/avatar", content);

            // Then it responds 404 — the object-level existence check, never a foreign-key 500.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task FromPackToAnUnknownPersonaIs404()
        {
            // Given no persona seeded at all,
            await using var factory = new PersonaAvatarWebFactory(personaStore: new FakePersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When POST .../from-pack names an id no persona holds,
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.UnknownPersonaId}/avatar/from-pack",
                new { packSlug = PersonaAvatarFixtures.PackSlug, itemName = PersonaAvatarFixtures.ItemName });

            // Then it responds 404 — checked BEFORE the pack/item are ever looked up.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task DeleteOfAnUnknownPersonaIs404()
        {
            // Given no face and no persona at all under this id,
            await using var factory = new PersonaAvatarWebFactory(personaStore: new FakePersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When DELETE /api/personas/{id}/avatar is called,
            var response = await client.DeleteAsync($"/api/personas/{PersonaAvatarFixtures.UnknownPersonaId}/avatar");

            // Then it responds 404 — the SAME "quiet, no oracle distinction beyond 404" this
            // controller's own class remarks establish; an unknown persona and a known persona with
            // no face read identically here.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioFromPackNamesAnUnknownPackOrItem
    {
        [Fact]
        public async Task AnUnknownPackSlugIs404()
        {
            // Given a known persona, but NO avatar pack ever installed,
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When POST .../from-pack names a pack slug that was never installed,
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar/from-pack",
                new { packSlug = "never-installed", itemName = PersonaAvatarFixtures.ItemName });

            // Then it responds 404.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task AnUnknownItemNameWithinAnInstalledPackIs404()
        {
            // Given a known persona and an installed pack that does NOT carry the requested item,
            var avatarPackStore = await PersonaAvatarFixtures.SeededAvatarPackStoreAsync();
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), avatarPackStore: avatarPackStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When POST .../from-pack names an item that pack never declared,
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar/from-pack",
                new { packSlug = PersonaAvatarFixtures.PackSlug, itemName = "no-such-item" });

            // Then it responds 404.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioAnOversizeBodyIsARegular400
    {
        [Fact]
        public async Task AnOversizeBodyIs400WithTheHonestTooLargeReason()
        {
            // Given a body one byte over ImageNormalizeService.MaxInputBytes,
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            using var content = PersonaAvatarFixtures.ImageBody(new byte[ImageNormalizeService.MaxInputBytes + 1]);

            // When it is PUT,
            var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused (400) naming the honest MiB-shaped reason — the SAME bounded-read
            // gate BoundedImportBodyReader.ReadBoundedBytesAsync enforces for every other route.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("MiB", body, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the admin read route (SPEC F128.9, PLAN T296)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheAdminReadRouteServesTheWornFace
    {
        [Fact]
        public async Task GetServesTheStoredBytesAsImagePng()
        {
            // Given a persona wearing a real face,
            var personaAvatarStore = new FakePersonaAvatarStore();
            var faceBytes = TestImages.CreatePng(512, 512);
            await personaAvatarStore.UpsertAsync(
                new PersonaAvatarInput(
                    PersonaAvatarFixtures.KnownPersonaId, faceBytes, "sha", "a-token", PersonaAvatarSource.Upload, null),
                CancellationToken.None);
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When GET /api/personas/{id}/avatar is called (the real production route),
            var response = await client.GetAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar");

            // Then it serves the exact stored bytes as image/png, stamped nosniff — the admin plane
            // carries no CSP (gh-#346), the SAME precedent CatalogController/FontEndpoints already
            // establish for their own served bytes.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(faceBytes, await response.Content.ReadAsByteArrayAsync());
            Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
            Assert.Equal("nosniff", Assert.Single(nosniff));
        }

        [Fact]
        public async Task AMatchingIfNoneMatchIsNotModified()
        {
            // Given a persona wearing a face, and its own ETag already read once,
            var personaAvatarStore = new FakePersonaAvatarStore();
            await personaAvatarStore.UpsertAsync(
                new PersonaAvatarInput(
                    PersonaAvatarFixtures.KnownPersonaId, TestImages.CreatePng(512, 512), "sha", "a-token",
                    PersonaAvatarSource.Upload, null),
                CancellationToken.None);
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            var firstResponse = await client.GetAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar");
            var etag = firstResponse.Headers.ETag ?? throw new InvalidOperationException("First GET carried no ETag.");

            // When the SAME route is asked again with If-None-Match set to that exact ETag,
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar");
            request.Headers.IfNoneMatch.Add(etag);
            var secondResponse = await client.SendAsync(request);

            // Then it responds 304, with no body re-sent — the framework's own conditional-request
            // handling off the token-derived EntityTag this route hands File(), never a hand-rolled
            // comparison (PersonaAvatarController.Get's own ETAG remarks).
            Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
            Assert.Empty(await secondResponse.Content.ReadAsByteArrayAsync());
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the admin read route has no face, or no session, to serve it to (T296)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheAdminReadRouteRefusesHonestly
    {
        [Fact]
        public async Task AFacelessPersonaIs404()
        {
            // Given a known persona with no face at all,
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When GET /api/personas/{id}/avatar is called,
            var response = await client.GetAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar");

            // Then it responds 404 — the SAME "no oracle distinction beyond 404" posture this
            // controller's write actions already establish (an unknown persona id reads identically).
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task AnAnonymousRequestIs401()
        {
            // Given no logged-in session at all,
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = factory.CreateClient();

            // When GET /api/personas/{id}/avatar is called anonymously,
            var response = await client.GetAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar");

            // Then it responds 401 — the SAME AdminSurface+Settings gate every other action on this
            // controller already carries.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the T295-review riders folded in at T296
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheT295ReviewRidersHold
    {
        [Fact]
        public async Task FromPackPersistsTheCanonicalPackSlugNotTheRequestSpelling()
        {
            // Given — HONESTLY: no store in this codebase resolves a pack slug case-insensitively
            // today. AvatarPackRepository's own production query is exact-match (`where slug = @slug`
            // against station.avatar_pack's plain-text UNIQUE column) — a caller-typed spelling that
            // differs by case from the stored slug simply doesn't resolve at all, so
            // request.PackSlug and the resolved pack's own Slug are ALWAYS byte-identical against real
            // Postgres right now. CaseInsensitiveAvatarPackStore below is a test double standing in
            // for a resolution rule this repo doesn't have yet, not a model of reality — this fact is
            // a DEFENSIVE pin of the controller's own behavior (it persists the resolved pack's own
            // canonical Slug, never the request's raw PackSlug) against the day a future store DOES
            // relax to case-insensitive matching: the write-path discipline is already correct, and
            // this proves it stays correct even once that day arrives, rather than silently relying on
            // "the two strings happen to always match anyway" to keep passing by accident.
            var canonicalItemBytes = TestImages.CreatePng(512, 512);
            var canonicalPack = new AvatarPack(
                "Canonical-Pack-Slug", "{}", "Canonical-Pack-Slug", DateTime.UtcNow,
                [new AvatarPackItem(
                    PersonaAvatarFixtures.ItemName, null, canonicalItemBytes, canonicalItemBytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(canonicalItemBytes)))]);
            var personaAvatarStore = new FakePersonaAvatarStore();
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: personaAvatarStore,
                avatarPackStore: new CaseInsensitiveAvatarPackStore(canonicalPack));
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);

            // When POST .../from-pack names that pack with a DIFFERENTLY-CASED spelling that still
            // resolves to it,
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar/from-pack",
                new { packSlug = "canonical-pack-slug", itemName = PersonaAvatarFixtures.ItemName });
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the stored provenance carries the pack's own CANONICAL slug, never the request's
            // own spelling.
            var stored = await personaAvatarStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None);
            Assert.Equal("Canonical-Pack-Slug", stored?.ImportedFrom);
        }

        [Fact]
        public async Task AConcurrentDeleteBetweenUpsertAndReadBackIsAnHonest404NotACrash()
        {
            // Given a persona with no face yet, wired through a store whose read visibility blips
            // ONCE immediately after a write completes — simulating a concurrent DELETE landing
            // between this controller's own UpsertAsync and its own immediate re-read,
            var innerStore = new FakePersonaAvatarStore();
            var raceyStore = new RaceySinglePersonaAvatarStore(innerStore);
            await using var factory = new PersonaAvatarWebFactory(
                personaStore: PersonaAvatarFixtures.SeededPersonaStore(), personaAvatarStore: raceyStore);
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            using var content = PersonaAvatarFixtures.ImageBody(TestImages.CreatePng(512, 512));

            // When PUT /api/personas/{id}/avatar is called (the real production route),
            var response = await client.PutAsync($"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar", content);

            // Then it responds 404 — an honest "it's gone again", never a 500 crash off the old
            // UnreachableException — even though the write itself genuinely reached the store
            // underneath (proven by reading the WRAPPED store directly, bypassing the one-shot blip).
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotNull(await innerStore.GetByPersonaIdAsync(PersonaAvatarFixtures.KnownPersonaId, CancellationToken.None));
        }

        [Fact]
        public async Task UnknownPackAndItemProblemsClampTheEchoedStrings()
        {
            // Given a known persona, no avatar pack installed, and a hostile packSlug carrying a
            // control character plus a long run well over LogSafeText.MaxLength,
            await using var factory = new PersonaAvatarWebFactory(personaStore: PersonaAvatarFixtures.SeededPersonaStore());
            var client = await PersonaAvatarWebFactory.LoggedInClientAsync(factory);
            var hostilePackSlug = "line1\nline2" + new string('x', 500);

            // When POST .../from-pack names it,
            var response = await client.PostAsJsonAsync(
                $"/api/personas/{PersonaAvatarFixtures.KnownPersonaId}/avatar/from-pack",
                new { packSlug = hostilePackSlug, itemName = PersonaAvatarFixtures.ItemName });
            var body = await response.Content.ReadAsStringAsync();

            // Then the 404's own Detail carries no raw control character (JSON-escaped or not) and no
            // 250-char run of the hostile filler — the echoed slug was clamped through
            // LogSafeText.Sanitize, never interpolated verbatim.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("\\n", body, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('x', 250), body, StringComparison.Ordinal);
        }
    }

    // ── ffprobe/ffmpeg verification helpers — black-box: probe the produced bytes, never the
    // service's internals (mirrors GenWave.Tts.Tests' own Story327_TwoVoicesOneClip idiom). ──────

    static async Task<(int Width, int Height, string Codec)> ProbePngAsync(byte[] pngBytes)
    {
        var path = await WriteTempFileAsync(pngBytes);
        try
        {
            var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-show_entries");
            psi.ArgumentList.Add("stream=width,height,codec_name");
            psi.ArgumentList.Add("-of");
            psi.ArgumentList.Add("default=noprint_wrappers=1");
            psi.ArgumentList.Add(path);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            int? width = null;
            int? height = null;
            string? codec = null;
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                switch (parts[0])
                {
                    case "width": width = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    case "height": height = int.Parse(parts[1], CultureInfo.InvariantCulture); break;
                    case "codec_name": codec = parts[1]; break;
                }
            }

            if (width is null || height is null || codec is null)
                throw new InvalidOperationException($"ffprobe produced no usable stream info: {stdout}");

            return (width.Value, height.Value, codec);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Reads back the single RGB pixel at (<paramref name="x"/>, <paramref name="y"/>) via
    /// a 1×1 ffmpeg crop piped to raw stdout — the same "shell to the real binary, parse its
    /// output" idiom <c>Story327_TwoVoicesOneClip.ProbeFlatFactorAsync</c> already uses.</summary>
    static async Task<(byte R, byte G, byte B)> SamplePixelAsync(byte[] pngBytes, int x, int y)
    {
        var path = await WriteTempFileAsync(pngBytes);
        try
        {
            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(
                $"crop=1:1:{x.ToString(CultureInfo.InvariantCulture)}:{y.ToString(CultureInfo.InvariantCulture)}");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("rawvideo");
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("rgb24");
            psi.ArgumentList.Add("-");

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var stdoutTask = ReadExactlyAsync(p.StandardOutput.BaseStream, 3);
            await p.StandardError.ReadToEndAsync();
            var pixel = await stdoutTask;
            await p.WaitForExitAsync();

            return (pixel[0], pixel[1], pixel[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
            if (n == 0) break;
            read += n;
        }

        return buffer;
    }

    static async Task<string> WriteTempFileAsync(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"genwave-story333-probe-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own T295 write-path Facts — boots
/// the real Program.cs graph with <see cref="IPersonaStore"/> replaced by a
/// <see cref="FakePersonaStore"/>, <see cref="IPersonaAvatarStore"/> by a
/// <see cref="FakePersonaAvatarStore"/>, and <see cref="IAvatarPackStore"/> by a
/// <see cref="FakeAvatarPackStore"/> (mirrors Story332_AvatarPacksIntoTheLibrary.cs's own
/// <c>AvatarPackInstallWebFactory</c>). <see cref="ImageNormalizeService"/> is left WIRED to its real
/// production registration (real ffmpeg) — never faked, mirrors this file's own T291 posture.
/// </summary>
file sealed class PersonaAvatarWebFactory(
    FakePersonaStore? personaStore = null, IPersonaAvatarStore? personaAvatarStore = null,
    IAvatarPackStore? avatarPackStore = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story333-avatarwrite";

    readonly FakePersonaStore personaStore = personaStore ?? PersonaAvatarFixtures.SeededPersonaStore();
    readonly IPersonaAvatarStore personaAvatarStore = personaAvatarStore ?? new FakePersonaAvatarStore();
    readonly IAvatarPackStore avatarPackStore = avatarPackStore ?? new FakeAvatarPackStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IPersonaStore>();
            services.AddSingleton<IPersonaStore>(personaStore);

            services.RemoveAll<IPersonaAvatarStore>();
            services.AddSingleton<IPersonaAvatarStore>(personaAvatarStore);

            services.RemoveAll<IAvatarPackStore>();
            services.AddSingleton<IAvatarPackStore>(avatarPackStore);
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>Fixture constants + tiny builders shared across this file's own T295 write-path Facts —
/// <c>file</c>-scoped, mirrors <c>AvatarPackInstallFixtures</c>'s own established idiom.</summary>
file static class PersonaAvatarFixtures
{
    public const long KnownPersonaId = 42;
    public const long UnknownPersonaId = 9999;

    public const string PackSlug = "warm-grins";
    public const string ItemName = "Classic";

    /// <summary>A <see cref="FakePersonaStore"/> seeded with exactly the one persona
    /// <see cref="KnownPersonaId"/> — the default arrangement most Facts in this file need; a Fact
    /// proving the 404 path instead passes a bare <c>new FakePersonaStore()</c> explicitly.</summary>
    public static FakePersonaStore SeededPersonaStore()
    {
        var store = new FakePersonaStore();
        store.Seed(new Persona(KnownPersonaId, "Test Persona", "", "", "", DateTime.UtcNow, DateTime.UtcNow));
        return store;
    }

    /// <summary>A <see cref="FakeAvatarPackStore"/> with one pack (<see cref="PackSlug"/>) installed,
    /// carrying one item (<see cref="ItemName"/>) — a real 512×512 PNG, hashed the same way
    /// <c>AvatarPackController</c>'s own install route would have stored it.</summary>
    public static async Task<FakeAvatarPackStore> SeededAvatarPackStoreAsync()
    {
        var store = new FakeAvatarPackStore();
        var itemBytes = TestImages.CreatePng(512, 512);
        await store.UpsertAsync(
            PackSlug, "{}", PackSlug,
            [new AvatarPackItemInput(ItemName, itemBytes, Convert.ToHexStringLower(SHA256.HashData(itemBytes)), null)],
            CancellationToken.None);
        return store;
    }

    /// <summary>Wraps <paramref name="bytes"/> as HTTP content under <paramref name="contentType"/> —
    /// PLAN T295's own "Content-Type is advisory only" posture means the exact value here never
    /// changes an outcome; <c>image/png</c> is the default purely so most call sites don't have to
    /// name it.</summary>
    public static ByteArrayContent ImageBody(byte[] bytes, string contentType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }
}

/// <summary>
/// Decorates a real <see cref="IPersonaAvatarStore"/> so its FIRST read immediately after an
/// <see cref="UpsertAsync"/> reports "no face" once, then reverts to normal — simulating a concurrent
/// <c>DELETE /api/personas/{id}/avatar</c> landing between <see cref="PersonaAvatarController"/>'s own
/// write and its immediate re-read (T295-review rider: <c>ToDtoResultAsync</c> must downgrade that race
/// to an honest 404, never crash on the old <see cref="System.Diagnostics.UnreachableException"/>). The
/// wrapped row is never actually removed — only this ONE read's own visibility blips — so a Fact using
/// this can still prove the write itself genuinely reached the store, by reading the WRAPPED instance
/// directly afterward.
/// </summary>
file sealed class RaceySinglePersonaAvatarStore(IPersonaAvatarStore inner) : IPersonaAvatarStore
{
    bool nextReadRacesPastTheRow;

    public Task<PersonaAvatar?> GetByPersonaIdAsync(long personaId, CancellationToken ct)
    {
        if (nextReadRacesPastTheRow)
        {
            nextReadRacesPastTheRow = false;
            return Task.FromResult<PersonaAvatar?>(null);
        }

        return inner.GetByPersonaIdAsync(personaId, ct);
    }

    /// <summary>Passes straight through — unexercised by this file's own race, which only ever
    /// drives <see cref="GetByPersonaIdAsync"/> through <c>PersonaAvatarController</c>'s post-write
    /// re-read.</summary>
    public Task<string?> GetTokenByPersonaIdAsync(long personaId, CancellationToken ct) =>
        inner.GetTokenByPersonaIdAsync(personaId, ct);

    public Task<PersonaAvatar?> GetByTokenAsync(string token, CancellationToken ct) =>
        inner.GetByTokenAsync(token, ct);

    public async Task UpsertAsync(PersonaAvatarInput avatar, CancellationToken ct)
    {
        await inner.UpsertAsync(avatar, ct);
        nextReadRacesPastTheRow = true;
    }

    public Task<bool> DeleteAsync(long personaId, CancellationToken ct) =>
        inner.DeleteAsync(personaId, ct);
}

/// <summary>
/// A minimal <see cref="IAvatarPackStore"/> double proving the T295-review rider: resolves
/// <see cref="GetBySlugAsync"/> CASE-INSENSITIVELY (a shape distinct from <see cref="FakeAvatarPackStore"/>'s
/// own ordinal dictionary, which can never produce a resolved <see cref="AvatarPack.Slug"/> that differs
/// from the exact string a caller looked it up with) but always returns its own CANONICAL
/// <paramref name="canonicalPack"/> — proof that <see cref="PersonaAvatarController.ApplyFromPack"/>
/// persists THAT value, never the raw request spelling that merely happened to resolve it. Only
/// <see cref="GetBySlugAsync"/> is ever exercised by that action; every other member is unused by this
/// file's one Fact that constructs this double.
/// </summary>
file sealed class CaseInsensitiveAvatarPackStore(AvatarPack canonicalPack) : IAvatarPackStore
{
    public Task<AvatarPack?> GetBySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(string.Equals(slug, canonicalPack.Slug, StringComparison.OrdinalIgnoreCase) ? canonicalPack : null);

    public Task UpsertAsync(
        string slug, string definition, string importedFrom, IReadOnlyList<AvatarPackItemInput> items, CancellationToken ct) =>
        throw new NotSupportedException("Unused by this double's one caller.");

    public Task<IReadOnlyList<AvatarPackSummary>> GetAllAsync(CancellationToken ct) =>
        throw new NotSupportedException("Unused by this double's one caller.");

    public Task<bool> DeleteAsync(string slug, CancellationToken ct) =>
        throw new NotSupportedException("Unused by this double's one caller.");
}

/// <summary>
/// A fake <see cref="IImageProcessRunner"/> that writes a genuine PNG-signed but deliberately
/// over-<see cref="ImageNormalizeService.MaxOutputBytes"/> file to whatever output path
/// <c>ImageNormalizeService.BuildFfmpegArgs</c> asked for — the only DETERMINISTIC way to
/// drive <c>RunFfmpegNormalizeAsync</c>'s own ceiling branch (gh-#520's <c>OutputTooLarge</c> reason):
/// a real ffmpeg re-encode at max settings never approaches the raised 768 KiB ceiling for any
/// fixture this test suite generates, by design (that is the whole fix). Never touches a real ffmpeg
/// process — models "ffmpeg genuinely succeeded, the result was merely too large" directly.
/// </summary>
file sealed class OversizeOutputImageProcessRunner : IImageProcessRunner
{
    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public Task RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        // BuildFfmpegArgs's own shape always ends with "--", outputPath — the last element is always
        // the file this fake must produce.
        var outputPath = args[^1];
        var oversizeButSignatureValid = new byte[ImageNormalizeService.MaxOutputBytes + 1];
        PngSignature.CopyTo(oversizeButSignatureValid, 0);
        File.WriteAllBytes(outputPath, oversizeButSignatureValid);
        return Task.CompletedTask;
    }
}
