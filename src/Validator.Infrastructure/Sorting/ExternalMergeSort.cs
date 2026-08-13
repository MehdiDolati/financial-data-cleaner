using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Domain.Candles;

namespace Validator.Infrastructure.Sorting
{
    public sealed class ExternalMergeSort
    {
        private readonly ITempStorage _tempStorage;

        public ExternalMergeSort(ITempStorage tempStorage)
        {
            _tempStorage = tempStorage ?? throw new ArgumentNullException(nameof(tempStorage));
        }

        public async Task<List<PriceCandle>> SortAsync(IEnumerable<PriceCandle> source, int chunkSize = 10_000)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            var chunks = new List<string>();
            var currentChunk = new List<PriceCandle>();

            foreach (var candle in source)
            {
                currentChunk.Add(candle);

                if (currentChunk.Count >= chunkSize)
                {
                    chunks.Add(WriteChunk(currentChunk));
                    currentChunk = new List<PriceCandle>();
                }
            }

            if (currentChunk.Count > 0)
            {
                chunks.Add(WriteChunk(currentChunk));
            }

            if (chunks.Count == 0)
            {
                return new List<PriceCandle>();
            }

            if (chunks.Count == 1)
            {
                var ordered = await ReadChunkAsync(chunks[0]);
                _tempStorage.DeleteIfExists(chunks[0]);
                return ordered;
            }

            var merged = new List<PriceCandle>();
            var sortedChunks = new List<List<PriceCandle>>();

            foreach (var chunkPath in chunks)
            {
                sortedChunks.Add(await ReadChunkAsync(chunkPath));
                _tempStorage.DeleteIfExists(chunkPath);
            }

            foreach (var candle in sortedChunks.SelectMany(chunk => chunk).OrderBy(c => c.Timestamp))
            {
                merged.Add(candle);
            }

            return merged;
        }

        private string WriteChunk(List<PriceCandle> chunk)
        {
            var path = _tempStorage.CreateTempFile("candle-sort", ".csv");
            using var writer = new StreamWriter(path, append: false);

            foreach (var candle in chunk.OrderBy(c => c.Timestamp))
            {
                writer.WriteLine($"{candle.Timestamp:O},{candle.Open},{candle.High},{candle.Low},{candle.Close},{candle.Volume}");
            }

            writer.Flush();
            return path;
        }

        private static async Task<List<PriceCandle>> ReadChunkAsync(string path)
        {
            var result = new List<PriceCandle>();

            if (!File.Exists(path))
            {
                return result;
            }

            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var pieces = line.Split(',');
                if (pieces.Length < 6)
                {
                    continue;
                }

                var timestamp = DateTimeOffset.Parse(pieces[0]);
                var open = decimal.Parse(pieces[1]);
                var high = decimal.Parse(pieces[2]);
                var low = decimal.Parse(pieces[3]);
                var close = decimal.Parse(pieces[4]);
                var volume = decimal.Parse(pieces[5]);

                result.Add(new PriceCandle(timestamp, open, high, low, close, volume));
            }

            return result;
        }
    }
}
