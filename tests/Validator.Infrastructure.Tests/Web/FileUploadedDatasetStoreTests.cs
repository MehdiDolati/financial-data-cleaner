using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Validator.Application.Ingestion;
using Validator.Domain.Candles;
using Validator.Infrastructure.Web;

namespace Validator.Infrastructure.Tests.Web;

// Content-addressed upload-store tests: write-once storage keyed by SHA-256,
// byte-identical replay through the existing CSV source, duplicate reuse of
// the same content reference, and safe base names with no path components
// (SC-008, FR-006, FR-030).
public class FileUploadedDatasetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fdc-web-uploads-" + Guid.NewGuid().ToString("N"));

    private static readonly byte[] CleanDailyBytes = Encoding.UTF8.GetBytes(
        "date,time,open,high,low,close,volume\n" +
        "2026.01.02,00:00,0.63421,0.63580,0.63310,0.63502,125000\n" +
        "2026.01.03,00:00,0.63502,0.63650,0.63420,0.63612,118000\n" +
        "2026.01.06,00:00,0.63612,0.63780,0.63550,0.63720,132000\n");

    private FileUploadedDatasetStore NewStore() => new(_root);

    [Fact]
    public async Task StoreAsync_writes_content_and_returns_the_identity()
    {
        var store = NewStore();

        var dataset = await store.StoreAsync("upload.csv", new MemoryStream(CleanDailyBytes));

        dataset.Identity.FileName.Should().Be("upload.csv");
        dataset.Identity.ByteSize.Should().Be(CleanDailyBytes.Length);
        dataset.Identity.Sha256.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        dataset.ContentReference.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenAsync_replays_byte_identical_bytes()
    {
        var store = NewStore();
        var dataset = await store.StoreAsync("upload.csv", new MemoryStream(CleanDailyBytes));

        var source = await store.OpenAsync(dataset, new CsvInputOptions { HasHeader = true, Delimiter = "comma" });

        source.Should().NotBeNull();
        source.Should().BeAssignableTo<Validator.Application.Abstractions.IPreparedCandleSource>();

        var candles = new System.Collections.Generic.List<PriceCandle>();
        await foreach (var candle in source.ReadAllAsync())
        {
            candles.Add(candle);
        }

        candles.Should().HaveCount(3);
        candles[0].Open.Should().Be(0.63421m);
        candles[0].Timestamp.Should().Be(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime());
    }

    [Fact]
    public async Task Duplicate_storage_of_identical_content_reuses_the_same_reference()
    {
        var store = NewStore();

        var first = await store.StoreAsync("first-upload.csv", new MemoryStream(CleanDailyBytes));
        var second = await store.StoreAsync("second-upload.csv", new MemoryStream(CleanDailyBytes));

        second.ContentReference.Should().Be(first.ContentReference,
            "identical content is content-addressed to one stored artifact");
        second.Identity.Sha256.Should().Be(first.Identity.Sha256);

        // Exactly one content file exists in the store.
        var uploadsDirectory = Path.Combine(_root, "uploads");
        Directory.Exists(uploadsDirectory).Should().BeTrue();
        Directory.GetFiles(uploadsDirectory).Should().ContainSingle();
    }

    [Fact]
    public async Task Different_content_produces_different_references()
    {
        var store = NewStore();

        var first = await store.StoreAsync("a.csv", new MemoryStream(CleanDailyBytes));
        var different = CleanDailyBytes.Concat(new byte[] { 32 }).ToArray();
        var second = await store.StoreAsync("a.csv", new MemoryStream(different));

        second.ContentReference.Should().NotBe(first.ContentReference);
    }

    [Fact]
    public async Task Safe_base_name_rejects_path_components()
    {
        var store = NewStore();

        var act = async () => await store.StoreAsync("..\\evil.csv", new MemoryStream(CleanDailyBytes));
        await act.Should().ThrowAsync<ArgumentException>();

        var act2 = async () => await store.StoreAsync("folder/name.csv", new MemoryStream(CleanDailyBytes));
        await act2.Should().ThrowAsync<ArgumentException>();

        var act3 = async () => await store.StoreAsync("C:\\temp\\name.csv", new MemoryStream(CleanDailyBytes));
        await act3.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Stored_bytes_are_write_once_a_second_store_does_not_rewrite()
    {
        var store = NewStore();
        var dataset = await store.StoreAsync("upload.csv", new MemoryStream(CleanDailyBytes));

        var filePath = Path.Combine(_root, "uploads", dataset.ContentReference);
        var firstWrite = File.GetLastWriteTimeUtc(filePath);

        // Re-storing the same content must not rewrite or delete the file.
        await store.StoreAsync("upload.csv", new MemoryStream(CleanDailyBytes));
        File.GetLastWriteTimeUtc(filePath).Should().Be(firstWrite);

        // The stored bytes still hash to the recorded identity.
        var bytes = await File.ReadAllBytesAsync(filePath);
        var sha = System.Security.Cryptography.SHA256.HashData(bytes);
        Convert.ToHexString(sha).ToLowerInvariant().Should().Be(dataset.Identity.Sha256);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}