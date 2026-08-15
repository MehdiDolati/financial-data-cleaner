using System;

namespace Validator.Application.Abstractions
{
    public interface ITempStorage : IDisposable
    {
        string CreateTempFile(string prefix, string extension);
        void DeleteIfExists(string path);
    }
}