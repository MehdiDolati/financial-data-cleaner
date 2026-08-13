using System.Threading.Tasks;
using Validator.Domain.Findings;

namespace Validator.Application.Abstractions
{
    public interface IValidationRule
    {
        Task<ValidationFinding[]> EvaluateAsync();
    }
}