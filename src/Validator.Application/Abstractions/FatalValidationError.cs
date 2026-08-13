using System;

namespace Validator.Application.Abstractions
{
    public enum FatalErrorKind
    {
        Usage,
        Configuration,
        Parsing,
        Internal
    }

    public sealed record FatalValidationError(FatalErrorKind Kind, string Message)
    {
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }
}