namespace Validator.Application.Scoring
{
    // The state of one metric in a scored run. NotApplicable and NotScored are
    // deliberately distinct: their causes differ and the report must tell them
    // apart. Neither is ever credited as a perfect score.
    public enum MetricScoreState
    {
        // The check ran and its population is positive, so a score exists.
        Scored = 0,

        // The underlying check did not run for this configuration; the reason is
        // the originating check's own explanation.
        NotApplicable = 1,

        // The check ran but the population was zero, so the rate is undefined.
        NotScored = 2
    }
}
