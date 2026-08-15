using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Validator.Application.Abstractions;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Findings
{
    public sealed class SpoolingFindingStore : IFindingSink, IFindingReader, IDisposable
    {
        private readonly string _path;
        private readonly object _syncRoot = new();

        public SpoolingFindingStore(ITempStorage? tempStorage = null)
        {
            if (tempStorage is null)
            {
                _path = Path.Combine(Path.GetTempPath(), $"validator-findings-{Guid.NewGuid():N}.jsonl");
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            else
            {
                _path = tempStorage.CreateTempFile("findings", ".jsonl");
            }
        }

        public Task AppendAsync(ValidationFinding finding)
        {
            if (finding is null)
            {
                throw new ArgumentNullException(nameof(finding));
            }

            lock (_syncRoot)
            {
                var line = JsonSerializer.Serialize(finding);
                File.AppendAllText(_path, line + Environment.NewLine);
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<ValidationFinding> ReadAllAsync()
        {
            if (!File.Exists(_path))
            {
                yield break;
            }

            using var reader = new StreamReader(_path);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var finding = JsonSerializer.Deserialize<ValidationFinding>(line);
                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
