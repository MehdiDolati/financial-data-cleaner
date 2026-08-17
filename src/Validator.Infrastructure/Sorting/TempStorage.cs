using System;
using System.IO;
using Validator.Application.Abstractions;

namespace Validator.Infrastructure.Sorting
{
    public sealed class TempStorage : ITempStorage
    {
        private readonly string _rootDirectory;

        public string RootDirectory => _rootDirectory;

        public TempStorage(string? rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? Path.Combine(Path.GetTempPath(), $"validator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootDirectory);
        }

        public string CreateTempFile(string prefix, string extension)
        {
            var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
            var filePath = Path.Combine(_rootDirectory, $"{prefix}-{Guid.NewGuid():N}{normalizedExtension}");
            File.WriteAllText(filePath, string.Empty);
            return filePath;
        }

        public void DeleteIfExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootDirectory))
            {
                try
                {
                    Directory.Delete(_rootDirectory, recursive: true);
                }
                catch (DirectoryNotFoundException)
                {
                    // Another owner already completed cleanup.
                }
            }
        }
    }
}
