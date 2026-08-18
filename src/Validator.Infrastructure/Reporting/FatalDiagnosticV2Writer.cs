using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Reporting;
using Validator.Domain.Findings;

namespace Validator.Infrastructure.Reporting
{
    // Renders one fatal diagnostic as a v2 JSON document. The document contains
    // no summary, no reconciliation, no isClean value, and always reports
    // findingSetComplete false, so a fatal outcome can never be mistaken for a
    // successful report. Every value is invariant and independent of host
    // locale, absolute paths, and wall-clock time.
    public sealed class FatalDiagnosticV2Writer
    {
        public async Task WriteAsync(
            FatalDiagnostic diagnostic,
            TextWriter destination,
            CancellationToken cancellationToken = default)
        {
            if (diagnostic is null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            await destination.WriteAsync(Render(diagnostic).AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        public string Render(FatalDiagnostic diagnostic)
        {
            if (diagnostic is null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("contractVersion", diagnostic.ContractVersion);
                writer.WriteString("status", diagnostic.Status);
                writer.WriteBoolean("findingSetComplete", diagnostic.FindingSetComplete);
                writer.WriteString("code", diagnostic.Code);
                writer.WriteString("failureClass", diagnostic.FailureClass.ToString());
                writer.WriteString("stage", diagnostic.Stage.ToString());
                writer.WriteString("reason", diagnostic.Reason);
                writer.WriteString("guidance", diagnostic.Guidance);

                if (diagnostic.Source is not null)
                {
                    writer.WriteStartObject("source");
                    writer.WriteString("fileName", diagnostic.Source.FileName);
                    if (diagnostic.Source.ByteSize.HasValue)
                    {
                        writer.WriteNumber("byteSize", diagnostic.Source.ByteSize.Value);
                    }

                    if (diagnostic.Source.Sha256 is not null)
                    {
                        writer.WriteString("sha256", diagnostic.Source.Sha256);
                    }

                    writer.WriteEndObject();
                }

                if (diagnostic.Location is not null)
                {
                    writer.WriteStartObject("location");
                    if (diagnostic.Location.SourceLine.HasValue)
                    {
                        writer.WriteNumber("sourceLine", diagnostic.Location.SourceLine.Value);
                    }

                    if (diagnostic.Location.TimestampUtc.HasValue)
                    {
                        writer.WriteString("timestampUtc", ToUtcText(diagnostic.Location.TimestampUtc.Value));
                    }

                    if (diagnostic.Location.Field is not null)
                    {
                        writer.WriteString("field", diagnostic.Location.Field);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteStartArray("checks");
                foreach (var check in diagnostic.Checks)
                {
                    writer.WriteStartObject();
                    writer.WriteString("check", check.Check.ToString());
                    writer.WriteString("status", check.Status.ToString());
                    if (check.Status != CheckStatus.Completed)
                    {
                        writer.WriteString("reason", check.Reason);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }

        internal static string ToUtcText(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
