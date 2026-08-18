namespace Validator.Application.Reporting
{
    /// <summary>
    /// The outcome of a validation run that completed.
    /// </summary>
    /// <remarks>
    /// There is deliberately no fatal member: a run that stopped early is a
    /// separate aggregate, so a partial run can never carry fields implying
    /// complete quality totals.
    /// </remarks>
    public enum ReportStatus
    {
        /// <summary>Every check ran and found nothing to report.</summary>
        Clean = 0,

        /// <summary>At least one check produced a finding.</summary>
        FindingsDetected = 1
    }

    /// <summary>
    /// Whether an individual check ran, so a reader can tell an absence of
    /// findings from an absence of checking.
    /// </summary>
    public enum CheckStatus
    {
        /// <summary>The check ran over the whole scanned range.</summary>
        Completed = 0,

        /// <summary>The check does not apply to this input or configuration.</summary>
        NotApplicable = 1,

        /// <summary>The check did not run, or did not finish, so its findings are unknown.</summary>
        NotCompleted = 2
    }
}