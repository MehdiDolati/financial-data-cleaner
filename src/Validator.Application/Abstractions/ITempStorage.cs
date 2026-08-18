using System;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// Temporary files used to spool findings that do not fit in memory.
    /// </summary>
    /// <remarks>
    /// Owned by the application layer so a run's scratch files are cleaned up
    /// even when it fails, and so tests can substitute their own storage.
    /// </remarks>
    public interface ITempStorage : IDisposable
    {
        /// <summary>Creates an empty temporary file and returns its path.</summary>
        string CreateTempFile(string prefix, string extension);

        /// <summary>Deletes the file if it is still present, without failing if it is not.</summary>
        void DeleteIfExists(string path);
    }
}