namespace Validator.Domain.Findings
{
    // Canonical ordered finding categories (increasing severity)
    public enum FindingCategory
    {
        Informational = 0,
        Minor = 1,
        Major = 2,
        Critical = 3
    }
}