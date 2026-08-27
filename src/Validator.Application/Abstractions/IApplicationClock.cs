using System;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// Application-owned injectable clock, replacing direct DateTimeOffset.UtcNow calls
    /// to ensure deterministic substantive output (Constitution III, Constitution IV).
    /// </summary>
    public interface IApplicationClock
    {
        /// <summary>Gets the current UTC time.</summary>
        DateTimeOffset UtcNow { get; }
    }
}
