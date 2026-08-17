namespace Validator.Application.Reporting
{
    // Successful report status. Fatal is deliberately a separate aggregate so
    // a partial run can never carry fields implying complete quality totals.
    public enum ReportStatus
    {
        Clean = 0,
        FindingsDetected = 1
    }

    public enum CheckStatus
    {
        Completed = 0,
        NotApplicable = 1,
        NotCompleted = 2
    }
}