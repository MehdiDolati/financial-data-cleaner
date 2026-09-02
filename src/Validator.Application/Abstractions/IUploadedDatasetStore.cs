using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Ingestion;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// The retained user source content: a safe base name, byte size, and
    /// SHA-256, plus the content-addressed locator of the stored bytes
    /// (data-model.md).
    /// </summary>
    public sealed record UploadedDataset(
        SourceIdentity Identity,
        string ContentReference);

    /// <summary>
    /// Port: retain uploaded bytes content-addressed and replay them for a
    /// run. Stored bytes are write-once - no validation, scoring, reporting,
    /// comparison, or export path may modify, repair, reorder, or overwrite
    /// them (FR-006, SC-008) - and replayed bytes are byte-identical to those
    /// hashed, so validation reads what was hashed.
    /// </summary>
    public interface IUploadedDatasetStore
    {
        /// <summary>
        /// Retains the content under a safe file name and returns its
        /// identity with the content-addressed reference.
        /// </summary>
        ValueTask<UploadedDataset> StoreAsync(
            string safeFileName,
            Stream content,
            CancellationToken ct = default);

        /// <summary>
        /// Opens a prepared candle source that replays the exact stored bytes
        /// through the existing CSV pipeline, so validation reads what was
        /// hashed (SC-008).
        /// </summary>
        ValueTask<IPreparedCandleSource> OpenAsync(
            UploadedDataset dataset,
            CsvInputOptions options,
            CancellationToken ct = default);
    }
}