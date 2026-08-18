using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Infrastructure.Csv;
using Validator.Infrastructure.Findings;
using Validator.Infrastructure.Sorting;

namespace Validator.Infrastructure.Tests.Findings
{
    public class SpoolAndSourceIdentityTests
    {
        [Fact]
        public async Task SpoolWriter_CompleteThenRead_ReplaysLinesInAppendOrder()
        {
            using var tempStorage = new TempStorage();
            var writer = new SpoolWriter(tempStorage);
            var path = writer.Path;

            await writer.AppendLineAsync("first");
            await writer.AppendLineAsync("second");
            await writer.AppendLineAsync("third");
            await writer.CompleteAsync();

            var lines = new List<string>();
            await foreach (var line in new SpoolReader(writer.Path, writer.CompletionMarkerPath).ReadLinesAsync())
            {
                lines.Add(line);
            }

            Assert.Equal(new[] { "first", "second", "third" }, lines);
        }

        [Fact]
        public async Task SpoolWriter_DisposeAfterSuccess_DeletesTemporaryArtifact()
        {
            using var tempStorage = new TempStorage();
            var writer = new SpoolWriter(tempStorage);
            var path = writer.Path;

            await writer.AppendLineAsync("payload");
            await writer.CompleteAsync();
            Assert.True(File.Exists(path));

            await writer.DisposeAsync();
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task SpoolWriter_DisposeWithoutComplete_DeletesPartialArtifact()
        {
            using var tempStorage = new TempStorage();
            var writer = new SpoolWriter(tempStorage);
            var path = writer.Path;

            await writer.AppendLineAsync("partial");
            Assert.True(File.Exists(path));

            await writer.DisposeAsync();
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task SpoolWriter_CancelledAppend_ThrowsAndDisposeStillDeletes()
        {
            using var tempStorage = new TempStorage();
            var writer = new SpoolWriter(tempStorage);
            var path = writer.Path;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                writer.AppendLineAsync("never", cancellation.Token).AsTask());

            await writer.DisposeAsync();
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task SpoolReader_ReadsOnlyCompletedSpool()
        {
            using var tempStorage = new TempStorage();
            var writer = new SpoolWriter(tempStorage);

            Assert.Throws<InvalidOperationException>(() => new SpoolReader(writer.Path, writer.CompletionMarkerPath));

            await writer.DisposeAsync();
        }

        [Fact]
        public async Task SpoolWriter_AppendAfterComplete_IsRejected()
        {
            using var tempStorage = new TempStorage();
            var writer = new SpoolWriter(tempStorage);
            await writer.AppendLineAsync("done");
            await writer.CompleteAsync();

            Assert.Throws<InvalidOperationException>(() => writer.AppendLineAsync("late"));
        }

        [Fact]
        public async Task SourceIdentityProvider_ComputesSha256SizeAndSafeName()
        {
            var provider = new SourceIdentityProvider();
            var payload = "open,high,low,close,volume" + Environment.NewLine + "1,2,3,4,5" + Environment.NewLine;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));

            var identity = await provider.ComputeAsync(stream, @"C:\data\raw\daily.csv");

            var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            Assert.Equal("daily.csv", identity.FileName);
            Assert.Equal(payload.Length, identity.ByteSize);
            Assert.Equal(expected, identity.Sha256);
        }

        [Fact]
        public async Task SourceIdentityProvider_IsDeterministicAcrossRepeatedReads()
        {
            var provider = new SourceIdentityProvider();
            var payload = "a,b" + Environment.NewLine + "1,2" + Environment.NewLine;

            using var first = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            using var second = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            var left = await provider.ComputeAsync(first, "same.csv");
            var right = await provider.ComputeAsync(second, "same.csv");

            Assert.Equal(left, right);
        }

        [Fact]
        public async Task SourceIdentityProvider_RejectsMissingFileName()
        {
            var provider = new SourceIdentityProvider();
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("x"));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                provider.ComputeAsync(stream, "   ").AsTask());
        }

        [Fact]
        public async Task SpoolWriter_WithoutTempStorage_UsesIsolatedTempFile()
        {
            await using var writer = new SpoolWriter();
            Assert.False(string.IsNullOrWhiteSpace(writer.Path));
            Assert.True(File.Exists(writer.Path));
        }
    }
}