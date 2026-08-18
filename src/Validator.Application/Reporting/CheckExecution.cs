using System;
using Validator.Domain.Findings;

namespace Validator.Application.Reporting
{
    // One established check's execution state. Completed checks carry no
    // reason; NotApplicable and NotCompleted checks always do.
    public sealed record CheckExecution
    {
        public CheckName Check { get; }
        public CheckStatus Status { get; }
        public string? Reason { get; }

        public CheckExecution(CheckName check, CheckStatus status, string? reason = null)
        {
            if (status == CheckStatus.Completed)
            {
                if (reason is not null)
                {
                    throw new ArgumentException("A completed check carries no reason.", nameof(reason));
                }
            }
            else if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A non-completed check requires a reason.", nameof(reason));
            }

            Check = check;
            Status = status;
            Reason = reason;
        }
    }
}