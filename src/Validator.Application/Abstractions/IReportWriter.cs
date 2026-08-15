using System.Threading.Tasks;
using Validator.Application.Abstractions;

namespace Validator.Application.Abstractions
{
    public interface IReportWriter
    {
        Task WriteReportAsync(object report); // concrete contract to be defined by infrastructure
    }
}