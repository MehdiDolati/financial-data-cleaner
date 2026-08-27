using System;

namespace Validator.Application.Abstractions
{
    /// <summary>
    /// Default system clock implementation that returns the real UTC time.
    /// Use this in production code; use a test clock in unit tests.
    /// </summary>
    public sealed class SystemClock : IApplicationClock
    {
        /// <summary>Singleton instance for production use.</summary>
        public static readonly SystemClock Instance = new();

        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
