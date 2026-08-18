using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;

namespace Validator.Infrastructure.Reporting
{
    // Outcome of publishing one rendered report. A report is either committed
    // to its destination (or standard output) in full, or the publication fails
    // and the destination is left byte-for-byte unchanged.
    public abstract record ReportCommitResult
    {
        public sealed record Committed(string? DestinationPath) : ReportCommitResult;

        public sealed record Failed(FatalDiagnostic Diagnostic) : ReportCommitResult;
    }

    // Stages every rendered report into a temporary artifact before publishing
    // it. Renders never write directly to the destination or to standard
    // output, so a failure part-way through rendering can never leave a
    // partially written report where a consumer could read it. Successful file
    // destinations are replaced by a single atomic move; the staged artifact is
    // always removed, whether the run succeeded, failed, or was cancelled.
    public sealed class StageAndCommitWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly string? _destinationPath;
        private readonly string? _sourcePath;

        public StageAndCommitWriter(string? destinationPath, string? sourcePath = null)
        {
            _destinationPath = string.IsNullOrWhiteSpace(destinationPath) ? null : destinationPath;
            _sourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath;
        }

        public async Task<ReportCommitResult> PublishAsync(
            Func<TextWriter, CancellationToken, Task> render,
            TextWriter standardOutput,
            CancellationToken cancellationToken = default)
        {
            if (render is null)
            {
                throw new ArgumentNullException(nameof(render));
            }

            if (standardOutput is null)
            {
                throw new ArgumentNullException(nameof(standardOutput));
            }

            string? destinationFullPath = null;
            string stagingDirectory;
            if (_destinationPath is not null)
            {
                try
                {
                    destinationFullPath = Path.GetFullPath(_destinationPath);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return Fail("INVALID_ARGUMENT", "The report destination is not a usable file path.", exception.Message);
                }

                if (IsAlias(destinationFullPath))
                {
                    return Fail(
                        "INVALID_ARGUMENT",
                        "The report destination is the same file as the validated input.",
                        "Choose a report destination that differs from the input file.");
                }

                var directory = Path.GetDirectoryName(destinationFullPath);
                if (string.IsNullOrEmpty(directory))
                {
                    return Fail(
                        "INVALID_ARGUMENT",
                        "The report destination has no parent directory.",
                        "Provide a destination path whose parent directory can be created.");
                }

                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    return Fail(
                        "REPORT_COMMIT_FAILED",
                        "The report destination directory could not be prepared.",
                        exception.Message);
                }

                stagingDirectory = directory;
            }
            else
            {
                stagingDirectory = Path.GetTempPath();
            }

            var stagedPath = Path.Combine(
                stagingDirectory,
                $".validator-report-{Guid.NewGuid():N}.staged");

            try
            {
                try
                {
                    await using var stagedWriter = new StreamWriter(
                        new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None),
                        Utf8NoBom);
                    await render(stagedWriter, cancellationToken).ConfigureAwait(false);
                    await stagedWriter.FlushAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Fail(
                        "REPORT_RENDER_FAILED",
                        "The report could not be rendered completely.",
                        exception.Message);
                }

                if (destinationFullPath is null)
                {
                    try
                    {
                        await using var staged = new FileStream(
                            stagedPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read);
                        using var reader = new StreamReader(staged, Utf8NoBom);
                        var buffer = new char[8192];
                        int read;
                        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await standardOutput.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        }

                        await standardOutput.FlushAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        return Fail(
                            "REPORT_COMMIT_FAILED",
                            "The report could not be published to standard output.",
                            exception.Message);
                    }

                    return new ReportCommitResult.Committed(null);
                }

                try
                {
                    File.Move(stagedPath, destinationFullPath, overwrite: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return Fail(
                        "REPORT_COMMIT_FAILED",
                        "The rendered report could not be committed to its destination.",
                        exception.Message);
                }

                return new ReportCommitResult.Committed(destinationFullPath);
            }
            finally
            {
                DeleteIfExists(stagedPath);
            }
        }

        private bool IsAlias(string destinationFullPath)
        {
            if (_sourcePath is null)
            {
                return false;
            }

            string sourceFullPath;
            try
            {
                sourceFullPath = Path.GetFullPath(_sourcePath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }

            return string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase);
        }

        private static ReportCommitResult Fail(string code, string reason, string guidance) =>
            new ReportCommitResult.Failed(new FatalDiagnostic(code, reason, guidance));

        private static void DeleteIfExists(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temporary artifact.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of a temporary artifact.
            }
        }
    }
}
