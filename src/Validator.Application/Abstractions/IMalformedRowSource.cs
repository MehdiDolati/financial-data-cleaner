using Validator.Domain.Findings;

namespace Validator.Application.Abstractions;

public interface IMalformedRowSource
{
    IReadOnlyList<MalformedRow> MalformedRows { get; }
}