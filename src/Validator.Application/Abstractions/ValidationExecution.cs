namespace Validator.Application.Abstractions
{
    public sealed record ValidationExecution(bool Succeeded, FatalValidationError? FatalError = null);
}