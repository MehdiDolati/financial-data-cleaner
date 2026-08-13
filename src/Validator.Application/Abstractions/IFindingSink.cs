using System.Threading.Tasks;
using Validator.Domain.Findings;

namespace Validator.Application.Abstractions
{
    public interface IFindingSink
    {
        Task AppendAsync(ValidationFinding finding);
    }
}