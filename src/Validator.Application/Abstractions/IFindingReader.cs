using System.Collections.Generic;
using System.Threading.Tasks;
using Validator.Domain.Findings;

namespace Validator.Application.Abstractions
{
    public interface IFindingReader
    {
        IAsyncEnumerable<ValidationFinding> ReadAllAsync();
    }
}