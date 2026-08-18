using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Validator.Application.Ingestion;

namespace Validator.Application.Abstractions
{
    // Computes the deterministic identity of the exact source bytes: safe base
    // name, byte size, and SHA-256 fingerprint over the same readable handle
    // used to prepare validation data.
    public interface ISourceIdentityProvider
    {
        ValueTask<SourceIdentity> ComputeAsync(
            Stream sourceBytes,
            string fileName,
            CancellationToken cancellationToken = default);
    }
}