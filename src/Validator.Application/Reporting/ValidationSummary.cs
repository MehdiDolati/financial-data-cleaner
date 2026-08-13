namespace Validator.Application.Reporting
{
    public sealed record ValidationSummary(int TotalFindings, int MalformedRows, int ValidRows)
    {
        public bool IsClean => TotalFindings == 0 && MalformedRows == 0;
    }
}